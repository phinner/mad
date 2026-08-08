using Discord;
using Discord.Interactions;
using Mad.Discord;
using Mad.Log;

namespace Mad.Delete;

[Group("autodelete", "Put me on a channel and I'll keep it clear of old messages.")]
[RequireUserPermission(GuildPermission.ManageMessages)]
public sealed class AutoDeleteInteractionModule(AutoDeleteRuleService rules, LogNotifier notifier)
    : MadInteractionModule
{
    internal const string EnableDescription =
        "I sweep this channel and delete messages once they pass the age you set.";
    internal const string DisableDescription = "I stop sweeping this channel. Messages already there stay put.";
    internal const string ListDescription = "I list every channel on my round and the settings for each.";

    private const int RulesPerPage = 10;

    [SlashCommand("enable", EnableDescription)]
    public async Task Enable(
        [Summary("older-than", "How old a message must be before I delete it, from 1m (a minute) to 12d (12 days).")]
            TimeSpan olderThan,
        [Summary("target-user-type", "Only delete messages from this kind of author. Default: everyone.")]
            DiscordUserType? targetUserType = null,
        [Summary("include-pin", "Delete pinned messages too. Default: no, pins are kept.")] bool includePins = false
    )
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
                MadTheme.ConfirmButton($"mad:v0:autodelete:confirm:{options.ToCustomIdArguments()}"),
                MadTheme.CancelButton("mad:v0:autodelete:cancel")
            )
        );
    }

    [RequireInitiator]
    [ComponentInteraction("mad:v0:autodelete:confirm:*,*,*", ignoreGroupNames: true)]
    public async Task ConfirmEnable(int minutes, int target, int includePins)
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
                        $"{channel.Mention} was put on my round while this was open, so I've left it as it is. "
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
                $"Right, {channel.Mention} is on my round: {settings}. The first sweep runs within the minute."
            )
        );
        await notifier.NotifyConfigChangeAsync(
            guildId.Value,
            Context.User,
            $"put {channel.Mention} on the round: {settings}."
        );
    }

    [RequireInitiator]
    [ComponentInteraction("mad:v0:autodelete:cancel", ignoreGroupNames: true)]
    public Task CancelEnable() =>
        UpdateThemedAsync(MadTheme.InfoMessage("Left it alone. Nothing has been added to my round."));

    [SlashCommand("disable", DisableDescription)]
    public async Task Disable()
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

        if (await rules.DeleteByGuildAndChannelAsync(guildId.Value, channel.Id) == 0)
        {
            await RespondThemedAsync(
                MadTheme.InfoMessage(
                    $"{channel.Mention} isn't on my round, so there's nothing to stop. "
                        + "Run `/autodelete list` to see the channels that are."
                )
            );
            return;
        }

        await RespondThemedAsync(
            MadTheme.SuccessMessage(
                $"Taken off my round. I'll stop deleting in {channel.Mention}; whatever is still there stays."
            )
        );
        await notifier.NotifyConfigChangeAsync(guildId.Value, Context.User, $"took {channel.Mention} off the round.");
    }

    [SlashCommand("list", ListDescription)]
    public async Task List()
    {
        var guildId = await GetGuildIdAsync();
        if (guildId is null)
        {
            return;
        }

        await DeferAsync(ephemeral: true);
        await FollowupThemedAsync(await BuildListPageAsync(guildId.Value, 0));
    }

    [RequireInitiator]
    [ComponentInteraction("mad:v0:autodelete-list:*", ignoreGroupNames: true)]
    public async Task ListPage(int page)
    {
        var guildId = await GetGuildIdAsync();
        if (guildId is null)
        {
            return;
        }

        await UpdateThemedAsync(await BuildListPageAsync(guildId.Value, page));
    }

    private async Task<MessageComponent> BuildListPageAsync(ulong guildId, int requestedPage)
    {
        var guildRules = (await rules.SelectByGuildAsync(guildId)).OrderBy(rule => rule.ChannelId).ToArray();

        if (guildRules.Length == 0)
        {
            return MadTheme.InfoMessage(
                "Nothing on my round yet. Run `/autodelete enable` in a channel to put it on there."
            );
        }

        var pageCount = (int)Math.Ceiling(guildRules.Length / (double)RulesPerPage);
        var page = Math.Clamp(requestedPage, 0, pageCount - 1);
        var lines = guildRules
            .Skip(page * RulesPerPage)
            .Take(RulesPerPage)
            .Select(rule =>
                $"<#{rule.ChannelId}> - {MessageDeletionOptions.Describe(rule.OlderThan, rule.TargetUserType, rule.IncludePins)}."
            );

        var count = guildRules.Length == 1 ? "1 channel" : $"{guildRules.Length} channels";
        var pageLabel = pageCount > 1 ? $" - page {page + 1}/{pageCount}" : string.Empty;

        return MadTheme.InfoMessage(
            $"### **On my round** - {count}{pageLabel}\n{string.Join('\n', lines)}\n",
            MadTheme.PageButton("Previous", $"mad:v0:autodelete-list:{page - 1}", page == 0),
            MadTheme.PageButton("Next", $"mad:v0:autodelete-list:{page + 1}", page == pageCount - 1)
        );
    }
}
