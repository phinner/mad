using Mad.Discord;
using Mad.Settings;
using NetCord;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace Mad.Log;

[SlashCommand("logchannel", "Choose where I file my reports.")]
[RequireUserPermissions<ApplicationCommandContext>(Permissions.ManageMessages)]
public sealed class LogInteractionModule(GuildSettingsService settings, LogNotifier notifier)
    : MadApplicationCommandModule
{
    internal const string EnableDescription = "I post my sweep summaries and any setting changes in this channel.";
    internal const string DisableDescription = "I stop posting reports. My sweeps carry on as normal.";

    [SubSlashCommand("enable", EnableDescription)]
    public async Task Enable()
    {
        var guildId = await Context.GetGuildIdAsync();
        if (guildId is null)
        {
            return;
        }

        var channel = await Context.GetTextChannelAsync();
        if (channel is null)
        {
            return;
        }

        if (
            MadPermissions.MissingMessage(
                Context.Interaction.AppPermissions,
                MadPermissions.Log,
                channel,
                "file anything there"
            ) is
            { } missing
        )
        {
            await RespondThemedAsync(missing);
            return;
        }

        await settings.UpsertAsync(new GuildSettings(guildId.Value, channel.Id));
        await RespondThemedAsync(
            MadTheme.SuccessMessage(
                $"Right, reports go in {channel} from now on: a summary after every sweep that deletes "
                    + "something, and a note whenever someone changes my settings."
            )
        );
        await notifier.NotifyConfigChangeAsync(guildId.Value, Context.User, $"made {channel} the log channel.");
    }

    [SubSlashCommand("disable", DisableDescription)]
    public async Task Disable()
    {
        var guildId = await Context.GetGuildIdAsync();
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
