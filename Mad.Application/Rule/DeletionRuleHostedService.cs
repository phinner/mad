using Discord;
using Discord.WebSocket;
using Mad.Discord;
using Mad.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentry;

namespace Mad.Rule;

internal sealed class DeletionRuleHostedService(
    DiscordSocketClient client,
    IServiceScopeFactory scopeFactory,
    ILogger<DeletionRuleHostedService> logger
) : BackgroundService
{
    private const string AuditLogReason = "Mad automated message deletion rule";
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await DeleteEligibleMessagesAsync(cancellationToken);
                await Task.Delay(RunInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task DeleteEligibleMessagesAsync(CancellationToken cancellationToken)
    {
        var transaction = SentrySdk.StartTransaction("deletion-rule-job", "job.deletion");
        SentrySdk.ConfigureScope(scope => scope.Transaction = transaction);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var rules = scope.ServiceProvider.GetRequiredService<DeletionRuleService>();
            var guilds = rules.SelectGuildsWithRulesAsync(cancellationToken);

            await foreach (var guildId in guilds.WithCancellation(cancellationToken))
            {
                await DeleteForGuildAsync(guildId, transaction, cancellationToken);
            }

            transaction.Status ??= SpanStatus.Ok;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            transaction.Status = SpanStatus.Cancelled;
            throw;
        }
        catch (Exception exception)
        {
            transaction.Status = SpanStatus.InternalError;
            logger.LogError(exception, "Could not load message deletion rules.");
        }
        finally
        {
            transaction.Finish();
        }
    }

    private async Task DeleteForGuildAsync(
        ulong guildId,
        ISpan parent,
        CancellationToken cancellationToken
    )
    {
        var span = parent.StartChild("job.deletion.guild", $"guild {guildId}");
        MadTelemetry.ScannedGuilds.Add(1);
        try
        {
            var guild = client.GetGuild(guildId);
            await using var scope = scopeFactory.CreateAsyncScope();
            var rules = scope.ServiceProvider.GetRequiredService<DeletionRuleService>();
            if (guild is null)
            {
                MadTelemetry.StaleGuilds.Add(1);
                span.Status = SpanStatus.NotFound;
                var deletedRules = await rules.DeleteByGuildAsync(guildId, cancellationToken);
                logger.LogWarning(
                    "Could not find configured guild {GuildId}; removed {RuleCount} rules.",
                    guildId,
                    deletedRules
                );
                return;
            }

            var channels = rules.SelectChannelsWithRulesAsync(guildId, cancellationToken);
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 10,
            };
            await Parallel.ForEachAsync(
                channels,
                parallelOptions,
                async (channelId, childCancellationToken) =>
                    await DeleteForChannelAsync(guild, channelId, span, childCancellationToken)
            );

            span.Status ??= SpanStatus.Ok;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            span.Status = SpanStatus.Cancelled;
            throw;
        }
        catch (Exception exception)
        {
            span.Status = SpanStatus.InternalError;
            logger.LogError(
                exception,
                "Could not process deletion rules for guild {GuildId}.",
                guildId
            );
        }
        finally
        {
            span.Finish();
        }
    }

    private async ValueTask DeleteForChannelAsync(
        SocketGuild guild,
        ulong channelId,
        ISpan parent,
        CancellationToken cancellationToken
    )
    {
        var span = parent.StartChild("job.deletion.channel", $"channel {channelId}");
        MadTelemetry.ScannedChannels.Add(1);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<DeletionRuleService>();
            ITextChannel? channel = guild.GetTextChannel(channelId);
            if (channel is null)
            {
                MadTelemetry.StaleChannels.Add(1);
                span.Status = SpanStatus.NotFound;
                var deletedRules = await service.DeleteByGuildAndChannelAsync(
                    guild.Id,
                    channelId,
                    cancellationToken
                );
                logger.LogWarning(
                    "Could not find configured deletion channel {ChannelId} in guild {GuildId}; removed {RuleCount} rules.",
                    channelId,
                    guild.Id,
                    deletedRules
                );
                return;
            }

            var rules = await service.SelectByGuildAndChannelAsync(
                guild.Id,
                channelId,
                cancellationToken
            );
            if (rules.Count == 0)
            {
                span.Status = SpanStatus.Ok;
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var bulkDeletionThreshold = now.AddDays(-14);
            var requestOptions = new RequestOptions { CancelToken = cancellationToken };
            var messageChunks = channel.GetMessagesAsync(
                int.MaxValue,
                CacheMode.AllowDownload,
                requestOptions
            );
            var (scanned, deleted) = await ProcessAsync(
                messageChunks,
                channel,
                message =>
                {
                    if (message.CreatedAt <= bulkDeletionThreshold)
                    {
                        return EvaluatorResult.Stop;
                    }

                    var userType = GetUserType(message.Author);
                    return rules.Any(rule =>
                        rule.UserType == userType && message.CreatedAt <= now - rule.OlderThan
                    )
                        ? EvaluatorResult.Take
                        : EvaluatorResult.Skip;
                },
                cancellationToken
            );

            MadTelemetry.ScannedMessages.Add(scanned);
            MadTelemetry.DeletedMessages.Add(deleted);
            span.SetData("messages.scanned", scanned);
            span.SetData("messages.deleted", deleted);
            span.Status = SpanStatus.Ok;

            if (deleted > 0 && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Deleted {MessageCount} messages from channel {ChannelId} in guild {GuildId}.",
                    deleted,
                    channelId,
                    guild.Id
                );
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            span.Status = SpanStatus.Cancelled;
            throw;
        }
        catch (Exception exception)
        {
            span.Status = SpanStatus.InternalError;
            logger.LogError(
                exception,
                "Could not delete messages from channel {ChannelId} in guild {GuildId}.",
                channelId,
                guild.Id
            );
        }
        finally
        {
            span.Finish();
        }
    }

    private static async Task<(int Scanned, int Deleted)> ProcessAsync(
        IAsyncEnumerable<IReadOnlyCollection<IMessage>> chunks,
        ITextChannel channel,
        Func<IMessage, EvaluatorResult> evaluator,
        CancellationToken cancellationToken
    )
    {
        var scanned = 0;
        var deleted = 0;

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            var deleting = new List<IMessage>();
            var stop = false;

            foreach (var message in chunk)
            {
                scanned++;
                var result = evaluator(message);
                switch (result)
                {
                    case EvaluatorResult.Stop:
                        stop = true;
                        break;
                    case EvaluatorResult.Skip:
                        break;
                    case EvaluatorResult.Take:
                        deleting.Add(message);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown filter result {result}.");
                }

                if (stop)
                {
                    break;
                }
            }

            if (deleting.Count > 0)
            {
                await BulkDeleteAsync(channel, deleting, cancellationToken);
                deleted += deleting.Count;
            }

            if (stop)
            {
                break;
            }
        }

        return (scanned, deleted);
    }

    private static Task BulkDeleteAsync(
        ITextChannel channel,
        List<IMessage> messages,
        CancellationToken cancellationToken
    )
    {
        var requestOptions = CreateRequestOptions(cancellationToken);
        return messages.Count == 1
            ? channel.DeleteMessageAsync(messages[0], requestOptions)
            : channel.DeleteMessagesAsync(messages, requestOptions);
    }

    private enum EvaluatorResult
    {
        Take,
        Skip,
        Stop,
    }

    private static DiscordUserType GetUserType(IUser author) =>
        author.IsWebhook ? DiscordUserType.Webhook
        : author.IsBot ? DiscordUserType.Bot
        : DiscordUserType.User;

    private static RequestOptions CreateRequestOptions(CancellationToken cancellationToken) =>
        new() { AuditLogReason = AuditLogReason, CancelToken = cancellationToken };
}
