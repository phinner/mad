using System.Globalization;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Mad.Discord;

namespace Mad.Rule;

[Group("rule", "Manage message deletion rules.")]
public sealed class DeletionRuleInteractionModule(DeletionRuleService rules)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const int RulesPerPage = 10;
    private static readonly TimeSpan MinimumOlderThan = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaximumOlderThan = TimeSpan.FromDays(12);

    [RequireUserPermission(GuildPermission.ManageMessages)]
    [SlashCommand("create", "Create a message deletion rule.")]
    public async Task CreateRule(
        [Summary("name", "A unique name for this rule.")] string name,
        [Summary("olderThan", "How long messages are kept, from 1m through 12d.")]
            TimeSpan olderThan,
        [Summary("userType", "Only delete messages from users, bots, or webhooks.")]
            DiscordUserType userType,
        [Summary("channel", "The text channel to clean. Defaults to this channel.")]
            ITextChannel? channel = null
    )
    {
        var guildId = await GetGuildIdAsync();
        if (guildId is null)
        {
            return;
        }

        await DeferAsync(ephemeral: true);

        name = name.Trim().ToLowerInvariant();
        if (name.Length is < 3 or > 64)
        {
            await FollowupAsync("Rule names must be between 3 and 64 characters.", ephemeral: true);
            return;
        }

        if (
            olderThan < MinimumOlderThan
            || olderThan > MaximumOlderThan
            || olderThan.Ticks % TimeSpan.TicksPerMinute != 0
        )
        {
            await FollowupAsync(
                "OlderThan must be a whole number of minutes between `1m` and `12d`.",
                ephemeral: true
            );
            return;
        }

        channel ??= Context.Channel as ITextChannel;
        if (channel is null)
        {
            await FollowupAsync("Choose a text channel for this rule.", ephemeral: true);
            return;
        }

        var result = await rules.CreateAsync(guildId.Value, name, channel.Id, userType, olderThan);
        switch (result)
        {
            case DeletionRuleService.Result.Success:
                break;
            case DeletionRuleService.Result.ChannelLimit(var value):
                await FollowupAsync(
                    $"{channel.Mention} has reached its limit of {value} deletion rules.",
                    ephemeral: true
                );
                return;
            case DeletionRuleService.Result.GuildLimit(var value):
                await FollowupAsync(
                    $"This server has reached its limit of {value} deletion rules.",
                    ephemeral: true
                );
                return;
            case DeletionRuleService.Result.DuplicateName:
                await FollowupAsync(
                    $"A rule named `{name}` already exists in this server.",
                    ephemeral: true
                );
                return;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(result),
                    result,
                    "Unknown rule-creation result."
                );
        }

        await FollowupAsync(
            $"Created rule `{name}` for {channel.Mention}; {FormatUserType(userType)} messages older than {FormatDuration(olderThan)} will be deleted.",
            ephemeral: true
        );
    }

    [SlashCommand("list", "List message deletion rules.")]
    public async Task ListRules(
        [Summary("channel", "Only show rules for this text channel.")] ITextChannel? channel = null
    )
    {
        var guildId = await GetGuildIdAsync();
        if (guildId is null)
        {
            return;
        }

        await DeferAsync(ephemeral: true);
        var page = await BuildRuleListPageAsync(guildId.Value, channel?.Id, 0);
        await FollowupAsync(page.Content, ephemeral: true, components: page.Components);
    }

    [RequireUserPermission(GuildPermission.ManageMessages)]
    [SlashCommand("delete", "Delete a message deletion rule.")]
    public async Task DeleteRule([Summary("name", "The name of the rule to delete.")] string name)
    {
        var guildId = await GetGuildIdAsync();
        if (guildId is null)
        {
            return;
        }

        await DeferAsync(ephemeral: true);
        name = name.Trim().ToLowerInvariant();
        var deleted = await rules.DeleteAsync(guildId.Value, name);
        await FollowupAsync(
            deleted ? $"Deleted rule `{name}`." : $"No rule named `{name}` exists in this server.",
            ephemeral: true
        );
    }

    [RequireInitiatorCheck]
    [ComponentInteraction("mad:v0:rule-list:next-page:*,*", ignoreGroupNames: true)]
    public Task RuleListNextPage(string channelId, int page) =>
        UpdateRuleListAsync(channelId, page);

    [RequireInitiatorCheck]
    [ComponentInteraction("mad:v0:rule-list:prev-page:*,*", ignoreGroupNames: true)]
    public Task RuleListPreviousPage(string channelId, int page) =>
        UpdateRuleListAsync(channelId, page);

    private async Task UpdateRuleListAsync(string channelId, int page)
    {
        var guildId = await GetGuildIdAsync();
        if (guildId is null || !ulong.TryParse(channelId, out var parsedChannelId))
        {
            return;
        }

        var ruleListPage = await BuildRuleListPageAsync(
            guildId.Value,
            parsedChannelId == 0 ? null : parsedChannelId,
            page
        );

        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(message =>
        {
            message.Content = ruleListPage.Content;
            message.Components = ruleListPage.Components;
        });
    }

    private async Task<RuleListPage> BuildRuleListPageAsync(
        ulong guildId,
        ulong? channelId,
        int requestedPage
    )
    {
        var matchingRules = (await rules.SelectByGuildAsync(guildId))
            .Where(rule => channelId is null || rule.ChannelId == channelId)
            .OrderBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pageCount = Math.Max(1, (int)Math.Ceiling(matchingRules.Length / (double)RulesPerPage));
        var page = Math.Clamp(requestedPage, 0, pageCount - 1);
        var pageRules = matchingRules.Skip(page * RulesPerPage).Take(RulesPerPage);
        var content =
            matchingRules.Length == 0
                ? "No deletion rules match this filter."
                : $"Deletion rules - page {page + 1}/{pageCount}\n"
                    + string.Join(
                        '\n',
                        pageRules.Select(rule =>
                            $"• `{rule.Name}` — <#{rule.ChannelId}>, {FormatUserType(rule.UserType)}, after {FormatDuration(rule.OlderThan)}"
                        )
                    );

        var filter = channelId?.ToString(CultureInfo.InvariantCulture) ?? "0";
        var components = new ComponentBuilder()
            .WithButton(
                "Previous",
                $"mad:v0:rule-list:prev-page:{filter},{page - 1}",
                ButtonStyle.Secondary,
                disabled: page == 0
            )
            .WithButton(
                "Next",
                $"mad:v0:rule-list:next-page:{filter},{page + 1}",
                ButtonStyle.Secondary,
                disabled: page == pageCount - 1
            )
            .Build();

        return new RuleListPage(content, components);
    }

    private async Task<ulong?> GetGuildIdAsync()
    {
        if (Context.Guild is not null)
        {
            return Context.Guild.Id;
        }

        await RespondAsync("This command can only be used in a server.", ephemeral: true);
        return null;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var parts = new List<string>(3);
        if (duration.Days > 0)
        {
            parts.Add($"{duration.Days}d");
        }
        if (duration.Hours > 0)
        {
            parts.Add($"{duration.Hours}h");
        }
        if (duration.Minutes > 0)
        {
            parts.Add($"{duration.Minutes}m");
        }
        return string.Join(' ', parts);
    }

    private static string FormatUserType(DiscordUserType userType) =>
        userType.ToString().ToLowerInvariant();

    private sealed record RuleListPage(string Content, MessageComponent Components);
}
