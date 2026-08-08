using Mad.Delete;
using Mad.Discord;
using Mad.Log;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace Mad.Help;

[RequireUserPermissions<ApplicationCommandContext>(Permissions.ManageMessages)]
public sealed class HelpInteractionModule(GatewayClient discord, CommandMentions commandMentions)
    : MadApplicationCommandModule
{
    [SlashCommand("help", "What I do, and the commands I take.")]
    public Task Help()
    {
        var user = discord.Cache.User;
        var avatarUrl = user?.GetAvatarUrl()?.ToString() ?? user?.DefaultAvatarUrl.ToString() ?? string.Empty;

        return RespondThemedAsync(
            MadTheme.Message(
                MadTheme.Info,
                [
                    new ComponentSectionProperties(
                        new ComponentSectionThumbnailProperties(new ComponentMediaProperties(avatarUrl)),
                        [
                            new TextDisplayProperties(
                                "# **M.A.D - Message Auto Delete**\nI delete messages. That's pretty much it."
                            ),
                        ]
                    ),
                    new ComponentSeparatorProperties { Spacing = ComponentSeparatorSpacingSize.Large },
                    new TextDisplayProperties(
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
                    new ComponentSeparatorProperties { Spacing = ComponentSeparatorSpacingSize.Large },
                    new TextDisplayProperties(
                        "-# Every command needs the **Manage Messages** permission, and I need **View Channel**, "
                            + "**Read Message History** and **Manage Messages** in the channels I look after."
                    ),
                    new ActionRowProperties([
                        new LinkButtonProperties("https://github.com/phinner/mad", "Source Code & Support"),
                    ]),
                ]
            ),
            ephemeral: false
        );

        string Mention(string command, string? subcommand = null)
        {
            var path = subcommand is null ? command : $"{command} {subcommand}";
            return commandMentions.TryGetId(command, out var commandId) ? $"</{path}:{commandId}>" : $"`/{path}`";
        }
    }
}
