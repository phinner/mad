using Discord;
using Discord.WebSocket;
using Mad.Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var rules = scope.ServiceProvider.GetRequiredService<DeletionRuleService>();
            var guilds = rules.SelectGuildsWithRulesAsync(cancellationToken);

            await foreach (var guildId in guilds.WithCancellation(cancellationToken))
            {
                await DeleteForGuildAsync(guildId, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not load message deletion rules.");
        }
    }

    private async Task DeleteForGuildAsync(ulong guildId, CancellationToken cancellationToken)
    {
        try
        {
            var guild = client.GetGuild(guildId);
            await using var scope = scopeFactory.CreateAsyncScope();
            var rules = scope.ServiceProvider.GetRequiredService<DeletionRuleService>();
            if (guild is null)
            {
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
                    await DeleteForChannelAsync(guild, channelId, childCancellationToken)
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not process deletion rules for guild {GuildId}.",
                guildId
            );
        }
    }

    private async ValueTask DeleteForChannelAsync(
        SocketGuild guild,
        ulong channelId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<DeletionRuleService>();
            ITextChannel? channel = guild.GetTextChannel(channelId);
            if (channel is null)
            {
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
            var deleted = await ProcessAsync(
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
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not delete messages from channel {ChannelId} in guild {GuildId}.",
                channelId,
                guild.Id
            );
        }
    }

    private static async Task<int> ProcessAsync(
        IAsyncEnumerable<IReadOnlyCollection<IMessage>> chunks,
        ITextChannel channel,
        Func<IMessage, EvaluatorResult> evaluator,
        CancellationToken cancellationToken
    )
    {
        var deleted = 0;

        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            var deleting = new List<IMessage>();
            var stop = false;

            foreach (var message in chunk)
            {
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

        return deleted;
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
