using Discord;
using Discord.Interactions;
using Mad.Discord;
using Mad.Settings;

namespace Mad.Log;

[Group("logchannel", "Choose where I file my reports.")]
[RequireUserPermission(GuildPermission.ManageMessages)]
public sealed class LogInteractionModule(GuildSettingService settings, LogNotifier notifier) : MadInteractionModule
{
    internal const string EnableDescription = "I post my sweep summaries and any setting changes in this channel.";
    internal const string DisableDescription = "I stop posting reports. My sweeps carry on as normal.";

    [SlashCommand("enable", EnableDescription)]
    public async Task Enable()
    {
        var guildId = await GetGuildIdAsync();
        if (guildId is null)
        {
            return;
        }

        var channel = await GetTextChannelAsync();
        if (channel is null)
        {
            return;
        }

        var permissions = Context.Guild.CurrentUser.GetPermissions(channel);
        if (!permissions.ViewChannel || !permissions.SendMessages)
        {
            await RespondThemedAsync(
                MadTheme.ErrorMessage(
                    $"I need **View Channel** and **Send Messages** in {channel.Mention} before I can file "
                        + "anything there. Grant those and run this again."
                )
            );
            return;
        }

        await settings.UpsertAsync(new GuildSetting(guildId.Value, channel.Id));
        await RespondThemedAsync(
            MadTheme.SuccessMessage(
                $"Right, reports go in {channel.Mention} from now on: a summary after every sweep that deletes "
                    + "something, and a note whenever someone changes my settings."
            )
        );
        await notifier.NotifyConfigChangeAsync(guildId.Value, Context.User, $"made {channel.Mention} the log channel.");
    }

    [SlashCommand("disable", DisableDescription)]
    public async Task Disable()
    {
        var guildId = await GetGuildIdAsync();
        if (guildId is null)
        {
            return;
        }

        var existing = await settings.SelectAsync(guildId.Value);
        if (existing?.LogChannelId is null)
        {
            await RespondThemedAsync(
                MadTheme.InfoMessage(
                    "No log channel is set, so there's nothing to switch off. "
                        + "Run `/logchannel enable` in a channel to have my reports sent there."
                )
            );
            return;
        }

        // File the farewell while the channel is still configured, or the notifier has nowhere to send it.
        await notifier.NotifyConfigChangeAsync(
            guildId.Value,
            Context.User,
            "switched the log channel off. This is the last report I'll file here."
        );
        await settings.UpsertAsync(existing with { LogChannelId = null });

        await RespondThemedAsync(
            MadTheme.SuccessMessage(
                "Done, no more reports. I'll keep sweeping as normal - check on me with `/autodelete list`."
            )
        );
    }
}
