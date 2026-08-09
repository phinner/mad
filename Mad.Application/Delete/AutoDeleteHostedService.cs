using System.Collections.Concurrent;
using System.Net;
using Mad.Discord;
using Mad.Launch;
using Mad.Log;
using Mad.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Mad.Delete;

internal sealed class AutoDeleteHostedService(
    GatewayClient client,
    RestClient rest,
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
        client.GuildDelete += ForgetGuildAsync;
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
            client.GuildDelete -= ForgetGuildAsync;
        }
    }

    private ValueTask SignalClientReadyAsync(ReadyEventArgs ready)
    {
        _clientEverReady = true;
        return ValueTask.CompletedTask;
    }

    private async ValueTask ForgetGuildAsync(GuildDeleteEventArgs guild)
    {
        if (guild.IsUnavailable)
        {
            return;
        }

        try
        {
            var deletedRules = await ForgetGuildSettingsAsync(guild.GuildId);
            logger.LogInformation(
                "Left guild {GuildId}; removed {RuleCount} rules and its settings.",
                guild.GuildId,
                deletedRules
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not remove the configuration for departed guild {GuildId}.",
                guild.GuildId
            );
        }
    }

    private async Task DeleteEligibleMessagesAsync(CancellationToken cancellationToken)
    {
        if (!_clientEverReady)
        {
            logger.LogDebug("Skipping deletion run; the Discord client has not become ready yet.");
            return;
        }

        using var deleteJobScope = SentrySdk.PushScope();
        var transaction = SentrySdk.StartTransaction("deletion-rule-job", "job.deletion");
        SentrySdk.ConfigureScope(scope => scope.Transaction = transaction);
        try
        {
            AutoDeleteRule? cursor = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<AutoDeleteRule> batch;
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var rules = scope.ServiceProvider.GetRequiredService<AutoDeleteRuleService>();
                    batch = await rules.SelectAllAsync(configuration.MaxChannelConcurrency, cursor, cancellationToken);
                }

                if (batch.Count == 0)
                {
                    break;
                }

                cursor = batch[^1];
                var cache = client.Cache;

                var handledMissingGuilds = new ConcurrentDictionary<ulong, byte>();
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = configuration.MaxChannelConcurrency,
                };
                await Parallel.ForEachAsync(
                    batch,
                    parallelOptions,
                    async (rule, childCancellationToken) =>
                        await DeleteForRuleAsync(cache, rule, handledMissingGuilds, transaction, childCancellationToken)
                );
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
            logger.LogError(exception, "Could not run automatic deletion rules.");
        }
        finally
        {
            transaction.Finish();
        }
    }

    private async ValueTask DeleteForRuleAsync(
        IGatewayClientCache cache,
        AutoDeleteRule rule,
        ConcurrentDictionary<ulong, byte> handledMissingGuilds,
        ISpan parent,
        CancellationToken cancellationToken
    )
    {
        var guildId = rule.GuildId;
        var channelId = rule.ChannelId;
        var span = parent.StartChild("job.deletion.rule", $"channel {channelId}");
        SentrySdk.Metrics.EmitCounter("mad.deletion.channels.scanned", 1L);
        try
        {
            if (!cache.Guilds.TryGetValue(guildId, out var guild))
            {
                span.Status = SpanStatus.Unavailable;
                if (!handledMissingGuilds.TryAdd(guildId, 0))
                {
                    return;
                }

                SentrySdk.Metrics.EmitCounter("mad.deletion.guilds.stale", 1L);
                try
                {
                    await rest.GetGuildAsync(guildId, cancellationToken: cancellationToken);
                    logger.LogDebug(
                        "Skipping guild {GuildId}; REST confirms it still exists but it is not in the gateway cache.",
                        guildId
                    );
                }
                catch (RestException exception) when (exception.StatusCode is HttpStatusCode.NotFound)
                {
                    var deletedRules = await ForgetGuildSettingsAsync(guildId, cancellationToken);
                    span.Status = SpanStatus.NotFound;
                    logger.LogWarning(
                        "Discord no longer has guild {GuildId}; removed {RuleCount} rules and its settings.",
                        guildId,
                        deletedRules
                    );
                }

                return;
            }

            if (guild.IsUnavailable)
            {
                span.Status = SpanStatus.Unavailable;
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Skipping guild {GuildId}; it is currently unavailable.", guildId);
                }
                return;
            }

            if (!guild.Channels.TryGetValue(channelId, out var cachedChannel) || cachedChannel is not TextGuildChannel)
            {
                SentrySdk.Metrics.EmitCounter("mad.deletion.channels.stale", 1L);
                span.Status = SpanStatus.NotFound;

                // The guild is available, so its channel cache is complete: the channel is really gone.
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<AutoDeleteRuleService>();
                var deletedRules = await service.DeleteByGuildAndChannelAsync(guildId, channelId, cancellationToken);
                logger.LogWarning(
                    "Could not find configured deletion channel {ChannelId} in guild {GuildId}; removed {RuleCount} rules.",
                    channelId,
                    guildId,
                    deletedRules
                );
                return;
            }

            if (!CanSweep(cache, guild, channelId))
            {
                SentrySdk.Metrics.EmitCounter("mad.deletion.channels.forbidden", 1L);
                span.Status = SpanStatus.PermissionDenied;
                if (rule.Accessible is RuleAccessibility.Yes)
                {
                    logger.LogWarning(
                        "Skipping channel {ChannelId} in guild {GuildId}; I no longer have the permissions to sweep it.",
                        channelId,
                        guildId
                    );
                }

                // The rule stays: the round picks the channel back up once the permissions come back.
                await MarkInaccessibleAsync(rule, cancellationToken);
                return;
            }

            if (rule.Accessible is not RuleAccessibility.Yes)
            {
                await SetAccessibleAsync(guildId, channelId, RuleAccessibility.Yes, cancellationToken);
                logger.LogInformation(
                    "Channel {ChannelId} in guild {GuildId} is back within reach; resuming its sweeps.",
                    channelId,
                    guildId
                );
            }

            var (scanned, deleted) = await MessageDeletionService.SweepAsync(
                rest,
                channelId,
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
                        guildId
                    );
                }

                await notifier.NotifySweepAsync(guildId, channelId, deleted, cancellationToken);
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
                "Could not process deletion rule for channel {ChannelId} in guild {GuildId}.",
                channelId,
                guildId
            );
        }
        finally
        {
            span.Finish();
        }
    }

    private static bool CanSweep(IGatewayClientCache cache, Guild guild, ulong channelId) =>
        cache.User?.Id is { } botId
        && guild.Users.TryGetValue(botId, out var bot)
        && bot.GetChannelPermissions(guild, channelId).HasFlag(MadPermissions.AutoDelete);

    private async ValueTask MarkInaccessibleAsync(AutoDeleteRule rule, CancellationToken cancellationToken)
    {
        if (rule.Accessible is RuleAccessibility.NoAndNotified)
        {
            return;
        }

        if (rule.Accessible is RuleAccessibility.Yes)
        {
            await SetAccessibleAsync(rule.GuildId, rule.ChannelId, RuleAccessibility.No, cancellationToken);
        }

        if (await notifier.NotifyInaccessibleAsync(rule.GuildId, rule.ChannelId, cancellationToken))
        {
            await SetAccessibleAsync(rule.GuildId, rule.ChannelId, RuleAccessibility.NoAndNotified, cancellationToken);
        }
    }

    private async ValueTask SetAccessibleAsync(
        ulong guildId,
        ulong channelId,
        RuleAccessibility accessible,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var rules = scope.ServiceProvider.GetRequiredService<AutoDeleteRuleService>();
        await rules.SetAccessibleAsync(guildId, channelId, accessible, cancellationToken);
    }

    private async Task<int> ForgetGuildSettingsAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var rules = scope.ServiceProvider.GetRequiredService<AutoDeleteRuleService>();
        var settings = scope.ServiceProvider.GetRequiredService<GuildSettingsService>();
        await settings.DeleteAsync(guildId, cancellationToken);
        return await rules.DeleteByGuildAsync(guildId, cancellationToken);
    }
}
