using Mad.Discord;
using Mad.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace Mad.Log;

public sealed class LogNotifier(
    GatewayClient client,
    RestClient rest,
    IServiceScopeFactory scopeFactory,
    ILogger<LogNotifier> logger
)
{
    public Task<bool> NotifySweepAsync(
        ulong guildId,
        ulong channelId,
        int deleted,
        CancellationToken cancellationToken = default
    ) =>
        NotifyAsync(
            guildId,
            MadTheme.SuccessMessage($"Swept <#{channelId}> on my round. {FormatMessageCount(deleted)} in the bin."),
            cancellationToken
        );

    public Task<bool> NotifyConfigChangeAsync(
        ulong guildId,
        User actor,
        string text,
        CancellationToken cancellationToken = default
    ) => NotifyAsync(guildId, MadTheme.InfoMessage($"{actor} {text}"), cancellationToken);

    public Task<bool> NotifyInaccessibleAsync(
        ulong guildId,
        ulong channelId,
        CancellationToken cancellationToken = default
    ) =>
        NotifyAsync(
            guildId,
            MadTheme.ErrorMessage(
                $"I've had to leave <#{channelId}> alone: I no longer have what I need to sweep it. "
                    + $"Give me {MadPermissions.Describe(MadPermissions.AutoDelete)} there and I'll pick it back up "
                    + "on my next round, or run `/autodelete disable` in it to take it off my round for good."
            ),
            cancellationToken
        );

    private async Task<bool> NotifyAsync(
        ulong guildId,
        IEnumerable<IMessageComponentProperties> components,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<GuildSettingsService>();
            var setting = await settings.SelectAsync(guildId, cancellationToken);
            if (setting?.LogChannelId is not { } logChannelId)
            {
                return false;
            }

            var cache = client.Cache;
            if (!cache.Guilds.TryGetValue(guildId, out var guild) || guild.IsUnavailable)
            {
                // The guild may simply not be cached yet; leave the setting alone.
                logger.LogDebug("Skipping log notification; guild {GuildId} is not available.", guildId);
                return false;
            }

            if (
                !guild.Channels.TryGetValue(logChannelId, out var cachedChannel)
                || cachedChannel is not TextGuildChannel
            )
            {
                logger.LogWarning(
                    "Log channel {ChannelId} no longer exists in guild {GuildId}; clearing the setting.",
                    logChannelId,
                    guildId
                );
                await settings.UpsertAsync(setting with { LogChannelId = null }, cancellationToken);
                return false;
            }

            var botId = cache.User?.Id;
            if (botId is null || !guild.Users.TryGetValue(botId.Value, out var bot))
            {
                logger.LogWarning(
                    "Skipping log notification; the bot user is not cached for guild {GuildId}.",
                    guildId
                );
                return false;
            }

            var permissions = bot.GetChannelPermissions(guild, logChannelId);
            if (!permissions.HasFlag(MadPermissions.Log))
            {
                logger.LogWarning(
                    "Cannot post to log channel {ChannelId} in guild {GuildId}; clearing the setting.",
                    logChannelId,
                    guildId
                );
                await settings.UpsertAsync(setting with { LogChannelId = null }, cancellationToken);
                return false;
            }

            await rest.SendMessageAsync(
                logChannelId,
                new MessageProperties
                {
                    Components = components,
                    Flags = MessageFlags.IsComponentsV2,
                    AllowedMentions = AllowedMentionsProperties.None,
                },
                cancellationToken: cancellationToken
            );
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not send a log notification for guild {GuildId}.", guildId);
            return false;
        }
    }

    private static string FormatMessageCount(int count) => count == 1 ? "1 message" : $"{count} messages";
}
