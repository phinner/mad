using Discord;
using Discord.WebSocket;
using Mad.Discord;
using Mad.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mad.Log;

/// <summary>
/// Reports work and configuration changes to the guild's log channel, when it has one. Every
/// method swallows its failures: a broken log channel must never stop a sweep or a command.
/// </summary>
public sealed class LogNotifier(
    DiscordSocketClient client,
    IServiceScopeFactory scopeFactory,
    ILogger<LogNotifier> logger
)
{
    public Task NotifySweepAsync(
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

    public Task NotifyConfigChangeAsync(
        ulong guildId,
        IUser actor,
        string text,
        CancellationToken cancellationToken = default
    ) => NotifyAsync(guildId, MadTheme.InfoMessage($"{actor.Mention} {text}"), cancellationToken);

    private async Task NotifyAsync(ulong guildId, MessageComponent components, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<GuildSettingService>();
            var setting = await settings.SelectAsync(guildId, cancellationToken);
            if (setting?.LogChannelId is not { } logChannelId)
            {
                return;
            }

            var guild = client.GetGuild(guildId);
            if (guild is null)
            {
                // The guild may simply not be cached yet; leave the setting alone.
                logger.LogDebug("Skipping log notification; guild {GuildId} is not available.", guildId);
                return;
            }

            var channel = guild.GetTextChannel(logChannelId);
            if (channel is null)
            {
                logger.LogWarning(
                    "Log channel {ChannelId} no longer exists in guild {GuildId}; clearing the setting.",
                    logChannelId,
                    guildId
                );
                await settings.UpsertAsync(setting with { LogChannelId = null }, cancellationToken);
                return;
            }

            var permissions = guild.CurrentUser.GetPermissions(channel);
            if (!permissions.ViewChannel || !permissions.SendMessages)
            {
                logger.LogWarning(
                    "Cannot post to log channel {ChannelId} in guild {GuildId}; clearing the setting.",
                    channel.Id,
                    guildId
                );
                await settings.UpsertAsync(setting with { LogChannelId = null }, cancellationToken);
                return;
            }

            await channel.SendMessageAsync(
                components: components,
                flags: MessageFlags.ComponentsV2,
                options: new RequestOptions { CancelToken = cancellationToken },
                allowedMentions: AllowedMentions.None
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not send a log notification for guild {GuildId}.", guildId);
        }
    }

    private static string FormatMessageCount(int count) => count == 1 ? "1 message" : $"{count} messages";
}
