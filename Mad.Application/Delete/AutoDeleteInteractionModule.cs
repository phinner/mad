using Mad.Discord;
using Mad.Log;
using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace Mad.Delete;

[SlashCommand("autodelete", "Put me on a channel and I'll keep it clear of old messages.")]
[RequireUserPermissions<ApplicationCommandContext>(Permissions.ManageMessages)]
public sealed class AutoDeleteInteractionModule(AutoDeleteRuleService rules, LogNotifier notifier)
    : MadApplicationCommandModule
{
    internal const string EnableDescription =
        "I sweep this channel and delete messages once they pass the age you set.";
    internal const string DisableDescription = "I stop sweeping this channel. Messages already there stay put.";
    internal const string ListDescription = "I list every channel on my round and the settings for each.";

    [SubSlashCommand("enable", EnableDescription)]
    public async Task Enable(
        [SlashCommandParameter(
            Name = "older-than",
            Description = "How old a message must be before I delete it, from 1m (a minute) to 12d (12 days)."
        )]
            TimeSpan olderThan,
        [SlashCommandParameter(
            Name = "target-user-type",
            Description = "Only delete messages from this kind of author. Default: everyone."
        )]
            DiscordUserType? targetUserType = null,
        [SlashCommandParameter(
            Name = "include-pin",
            Description = "Delete pinned messages too. Default: no, pins are kept."
        )]
            bool includePins = false
    )
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
                MadPermissions.AutoDelete,
                channel,
                "put it on my round"
            ) is
            { } missing
        )
        {
            await RespondThemedAsync(missing);
            return;
        }

        if (!MessageDeletionService.IsValidOlderThan(olderThan))
        {
            await RespondThemedAsync(
                MadTheme.ErrorMessage(
                    "I need a whole number of minutes between `1m` and `12d`. Seconds are too fine for me to work "
                        + "with. Try `30m`, `6h`, or `7d`."
                )
            );
            return;
        }

        var options = new MessageDeletionOptions(olderThan, targetUserType, includePins);
        await RespondThemedAsync(
            MadTheme.InfoMessage(
                $"Before I start, here's the job: I delete {options.Describe(channel)}.\n"
                    + "I sweep about once a minute and keep at it until you run `/autodelete disable` there.",
                MadTheme.ConfirmButton($"mad:v0:autodelete:confirm,{options.ToCustomIdArguments()}"),
                MadTheme.CancelButton("mad:v0:autodelete:cancel")
            )
        );
    }

    [SubSlashCommand("disable", DisableDescription)]
    public async Task Disable()
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

        if (await rules.DeleteByGuildAndChannelAsync(guildId.Value, channel.Id) == 0)
        {
            await RespondThemedAsync(
                MadTheme.InfoMessage(
                    $"{channel} isn't on my round, so there's nothing to stop. "
                        + "Run `/autodelete list` to see the channels that are."
                )
            );
            return;
        }

        await RespondThemedAsync(
            MadTheme.SuccessMessage(
                $"Taken off my round. I'll stop deleting in {channel}; whatever is still there stays."
            )
        );
        await notifier.NotifyConfigChangeAsync(guildId.Value, Context.User, $"took {channel} off the round.");
    }

    [SubSlashCommand("list", ListDescription)]
    public async Task List()
    {
        var guildId = await Context.GetGuildIdAsync();
        if (guildId is null)
        {
            return;
        }

        await DeferThemedAsync();
        await FollowupThemedAsync(await AutoDeleteList.BuildPageAsync(rules, guildId.Value, 0));
    }
}

[RequireUserPermissions<ButtonInteractionContext>(Permissions.ManageMessages)]
public sealed class AutoDeleteComponentInteractionModule(AutoDeleteRuleService rules, LogNotifier notifier)
    : MadComponentInteractionModule
{
    [RequireInitiator]
    [ComponentInteraction("mad:v0:autodelete:confirm")]
    public async Task ConfirmEnable(int minutes, int target, int includePins)
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
                MadPermissions.AutoDelete,
                channel,
                "put it on my round"
            ) is
            { } missing
        )
        {
            await UpdateThemedAsync(missing);
            return;
        }

        var options = MessageDeletionOptions.FromCustomIdArguments(minutes, target, includePins);
        if (options is null)
        {
            await UpdateThemedAsync(
                MadTheme.ErrorMessage(
                    "I can't read those settings back any more, so I've not touched the channel. "
                        + "Run `/autodelete enable` again."
                )
            );
            return;
        }

        var result = await rules.InsertAsync(
            guildId.Value,
            channel.Id,
            options.OlderThan,
            options.Target,
            options.IncludePins
        );

        switch (result)
        {
            case AutoDeleteRuleService.Result.Created:
                break;
            case AutoDeleteRuleService.Result.AlreadyExists:
                await UpdateThemedAsync(
                    MadTheme.ErrorMessage(
                        $"{channel} was put on my round while this was open, so I've left it as it is. "
                            + "Check it with `/autodelete list`, or run `/autodelete enable` again to replace those settings."
                    )
                );
                return;
            case AutoDeleteRuleService.Result.GuildLimit(var value):
                await UpdateThemedAsync(
                    MadTheme.ErrorMessage(
                        $"That's my limit: {value} channels on the round in this server. "
                            + "Run `/autodelete disable` in one of them to make room, then ask me again."
                    )
                );
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown autodelete insert result.");
        }

        var settings = MessageDeletionOptions.Describe(options.OlderThan, options.Target, options.IncludePins);
        await UpdateThemedAsync(
            MadTheme.SuccessMessage(
                $"Right, {channel} is on my round: {settings}. The first sweep runs within the minute."
            )
        );
        await notifier.NotifyConfigChangeAsync(guildId.Value, Context.User, $"put {channel} on the round: {settings}.");
    }

    [RequireInitiator]
    [ComponentInteraction("mad:v0:autodelete:cancel")]
    public Task CancelEnable() =>
        UpdateThemedAsync(MadTheme.InfoMessage("Left it alone. Nothing has been added to my round."));

    [RequireInitiator]
    [ComponentInteraction("mad:v0:autodelete-list")]
    public async Task ListPage(int page)
    {
        var guildId = await Context.GetGuildIdAsync();
        if (guildId is null)
        {
            return;
        }

        await UpdateThemedAsync(await AutoDeleteList.BuildPageAsync(rules, guildId.Value, page));
    }
}

internal static class AutoDeleteList
{
    private const int RulesPerPage = 10;

    public static async Task<IEnumerable<IMessageComponentProperties>> BuildPageAsync(
        AutoDeleteRuleService rules,
        ulong guildId,
        int requestedPage
    )
    {
        var guildRules = (await rules.SelectByGuildAsync(guildId))
            .OrderBy(rule => rule.Accessible is RuleAccessibility.Yes)
            .ThenBy(rule => rule.ChannelId)
            .ToArray();

        if (guildRules.Length == 0)
        {
            return MadTheme.InfoMessage(
                "Nothing on my round yet. Run `/autodelete enable` in a channel to put it on there."
            );
        }

        var pageCount = (int)Math.Ceiling(guildRules.Length / (double)RulesPerPage);
        var page = Math.Clamp(requestedPage, 0, pageCount - 1);
        var pageRules = guildRules.Skip(page * RulesPerPage).Take(RulesPerPage).ToArray();
        var lines = pageRules.Select(rule =>
        {
            var settings = MessageDeletionOptions.Describe(rule.OlderThan, rule.TargetUserType, rule.IncludePins);
            return rule.Accessible is RuleAccessibility.Yes
                ? $"<#{rule.ChannelId}> - {settings}."
                : $"⚠️ <#{rule.ChannelId}> - **left alone**, I can't get at it - {settings}.";
        });

        var count = guildRules.Length == 1 ? "1 channel" : $"{guildRules.Length} channels";
        var pageLabel = pageCount > 1 ? $" - page {page + 1}/{pageCount}" : string.Empty;
        var blocked = pageRules.Any(rule => rule.Accessible is not RuleAccessibility.Yes)
            ? $"\n-# ⚠️ I need {MadPermissions.Describe(MadPermissions.AutoDelete)} in a channel to sweep it.\n"
            : string.Empty;

        return MadTheme.InfoMessage(
            $"### **On my round** - {count}{pageLabel}\n{string.Join('\n', lines)}\n{blocked}",
            MadTheme.PageButton("Previous", $"mad:v0:autodelete-list,{page - 1}", page == 0),
            MadTheme.PageButton("Next", $"mad:v0:autodelete-list,{page + 1}", page == pageCount - 1)
        );
    }
}
