using Discord;
using Discord.WebSocket;
using Mad.Launch;
using Mad.Log;
using Mad.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mad.Delete;

internal sealed class AutoDeleteHostedService(
    DiscordSocketClient client,
    IServiceScopeFactory scopeFactory,
    LogNotifier notifier,
    MadConfiguration configuration,
    ILogger<AutoDeleteHostedService> logger
) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(1);

    private volatile bool _clientEverReady;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        client.Ready += SignalClientReadyAsync;
        client.LeftGuild += ForgetGuildAsync;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await DeleteEligibleMessagesAsync(cancellationToken);
                await Task.Delay(RunInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            client.Ready -= SignalClientReadyAsync;
            client.LeftGuild -= ForgetGuildAsync;
        }
    }

    private Task SignalClientReadyAsync()
    {
        _clientEverReady = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Leaving a guild is the only signal that its configuration is really gone. A sweep must never
    /// infer that from a cache miss, which an outage or a slow guild sync would also produce.
    /// </summary>
    private async Task ForgetGuildAsync(SocketGuild guild)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var rules = scope.ServiceProvider.GetRequiredService<AutoDeleteRuleService>();
            var settings = scope.ServiceProvider.GetRequiredService<GuildSettingService>();

            var deletedRules = await rules.DeleteByGuildAsync(guild.Id);
            await settings.DeleteAsync(guild.Id);

            logger.LogInformation(
                "Left guild {GuildId}; removed {RuleCount} rules and its settings.",
                guild.Id,
                deletedRules
            );
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not remove the configuration for departed guild {GuildId}.", guild.Id);
        }
    }

    private async Task DeleteEligibleMessagesAsync(CancellationToken cancellationToken)
    {
        if (!_clientEverReady || client.ConnectionState != ConnectionState.Connected)
        {
            logger.LogDebug("Skipping deletion run; the Discord client is not connected.");
            return;
        }

        // Own scope: interactions run concurrently with this job and would otherwise overwrite each
        // other's transaction on the shared one.
        using var sentryScope = SentrySdk.PushScope();
        var transaction = SentrySdk.StartTransaction("deletion-rule-job", "job.deletion");
        SentrySdk.ConfigureScope(scope => scope.Transaction = transaction);
        try
        {
            IReadOnlyList<ulong> guilds;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var rules = scope.ServiceProvider.GetRequiredService<AutoDeleteRuleService>();
                guilds = await rules.SelectGuildsWithRulesAsync(cancellationToken);
            }

            foreach (var guildId in guilds)
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
            logger.LogError(exception, "Could not load automatic deletion rules.");
        }
        finally
        {
            transaction.Finish();
        }
    }

    private async Task DeleteForGuildAsync(ulong guildId, ISpan parent, CancellationToken cancellationToken)
    {
        var span = parent.StartChild("job.deletion.guild", $"guild {guildId}");
        SentrySdk.Metrics.EmitCounter("mad.deletion.guilds.scanned", 1L);
        try
        {
            var guild = client.GetGuild(guildId);
            if (guild is null)
            {
                // Not cached does not mean gone: ForgetGuildAsync owns removing configuration.
                SentrySdk.Metrics.EmitCounter("mad.deletion.guilds.stale", 1L);
                span.Status = SpanStatus.NotFound;
                logger.LogWarning("Skipping guild {GuildId}; it is not in the cache.", guildId);
                return;
            }

            if (!guild.IsConnected)
            {
                span.Status = SpanStatus.Unavailable;
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Skipping guild {GuildId}; it is currently unavailable.", guildId);
                }
                return;
            }

            IReadOnlyList<AutoDeleteRule> guildRules;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var rules = scope.ServiceProvider.GetRequiredService<AutoDeleteRuleService>();
                guildRules = await rules.SelectByGuildAsync(guildId, cancellationToken);
            }

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = configuration.MaxChannelConcurrency,
            };
            await Parallel.ForEachAsync(
                guildRules,
                parallelOptions,
                async (rule, childCancellationToken) =>
                    await DeleteForChannelAsync(guild, rule, span, childCancellationToken)
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
            logger.LogError(exception, "Could not process deletion rules for guild {GuildId}.", guildId);
        }
        finally
        {
            span.Finish();
        }
    }

    private async ValueTask DeleteForChannelAsync(
        SocketGuild guild,
        AutoDeleteRule rule,
        ISpan parent,
        CancellationToken cancellationToken
    )
    {
        var channelId = rule.ChannelId;
        var span = parent.StartChild("job.deletion.channel", $"channel {channelId}");
        SentrySdk.Metrics.EmitCounter("mad.deletion.channels.scanned", 1L);
        try
        {
            ITextChannel? channel = guild.GetTextChannel(channelId);
            if (channel is null)
            {
                SentrySdk.Metrics.EmitCounter("mad.deletion.channels.stale", 1L);
                span.Status = SpanStatus.NotFound;

                // The guild is connected, so its channel cache is complete: the channel is really gone.
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<AutoDeleteRuleService>();
                var deletedRules = await service.DeleteByGuildAndChannelAsync(guild.Id, channelId, cancellationToken);
                logger.LogWarning(
                    "Could not find configured deletion channel {ChannelId} in guild {GuildId}; removed {RuleCount} rules.",
                    channelId,
                    guild.Id,
                    deletedRules
                );
                return;
            }

            var (scanned, deleted) = await MessageDeletionService.SweepAsync(
                channel,
                rule.OlderThan,
                rule.TargetUserType,
                rule.IncludePins,
                cancellationToken
            );

            SentrySdk.Metrics.EmitCounter("mad.deletion.messages.scanned", scanned);
            SentrySdk.Metrics.EmitCounter("mad.deletion.messages.deleted", deleted);
            span.SetData("messages.scanned", scanned);
            span.SetData("messages.deleted", deleted);
            span.Status = SpanStatus.Ok;

            if (deleted > 0)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Deleted {MessageCount} messages from channel {ChannelId} in guild {GuildId}.",
                        deleted,
                        channelId,
                        guild.Id
                    );
                }

                await notifier.NotifySweepAsync(guild.Id, channelId, deleted, cancellationToken);
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
}
