using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Mad.Delete;
using Mad.Discord;
using Mad.Launch;
using Mad.Log;

namespace Mad.Help;

[RequireUserPermission(GuildPermission.ManageMessages)]
public sealed class HelpInteractionModule(DiscordSocketClient discord, MadConfiguration configuration)
    : MadInteractionModule
{
    [SlashCommand("help", "What I do, and the commands I take.")]
    public async Task Help()
    {
        var commandIds = configuration.Debug
            ? (await Context.Guild.GetApplicationCommandsAsync()).ToDictionary(
                command => command.Name,
                command => command.Id
            )
            : (await discord.GetGlobalApplicationCommandsAsync()).ToDictionary(
                command => command.Name,
                command => command.Id
            );

        await RespondThemedAsync(
            MadTheme.Message(
                MadTheme.Info,
                [
                    new SectionBuilder()
                        .WithAccessory(
                            new ThumbnailBuilder(
                                new UnfurledMediaItemProperties(Context.Client.CurrentUser.GetDisplayAvatarUrl())
                            )
                        )
                        .AddComponent(
                            new TextDisplayBuilder(
                                "# **M.A.D - Message Auto Delete**\nI delete messages. That's pretty much it."
                            )
                        ),
                    new SeparatorBuilder(spacing: SeparatorSpacingSize.Large),
                    new TextDisplayBuilder(
                        $"""
                        {Mention("autodelete", "enable")}
                        > {AutoDeleteInteractionModule.EnableDescription}

                        {Mention("autodelete", "disable")}
                        > {AutoDeleteInteractionModule.DisableDescription}

                        {Mention("autodelete", "list")}
                        > {AutoDeleteInteractionModule.ListDescription}

                        {Mention("logchannel", "enable")}
                        > {LogInteractionModule.EnableDescription}

                        {Mention("logchannel", "disable")}
                        > {LogInteractionModule.DisableDescription}
                        """
                    ),
                    new SeparatorBuilder(spacing: SeparatorSpacingSize.Large),
                    new TextDisplayBuilder(
                        "-# Every command needs the **Manage Messages** permission, and I need **View Channel**, "
                            + "**Read Message History** and **Manage Messages** in the channels I look after."
                    ),
                    new ActionRowBuilder().AddComponent(
                        ButtonBuilder.CreateLinkButton("Source Code & Support", "https://github.com/phinner/mad")
                    ),
                ]
            ),
            ephemeral: false
        );
        return;

        // Global commands take a while to propagate after a deploy, so fall back to plain text
        // rather than throwing when Discord has not caught up yet.
        string Mention(string command, string? subcommand = null)
        {
            var path = subcommand is null ? command : $"{command} {subcommand}";
            return commandIds.TryGetValue(command, out var commandId) ? $"</{path}:{commandId}>" : $"`/{path}`";
        }
    }
}
