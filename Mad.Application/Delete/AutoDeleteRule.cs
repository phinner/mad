using Mad.Discord;
using Microsoft.EntityFrameworkCore;

namespace Mad.Delete;

[PrimaryKey(nameof(GuildId), nameof(ChannelId))]
public sealed record AutoDeleteRule(
    ulong GuildId,
    ulong ChannelId,
    TimeSpan OlderThan,
    DiscordUserType? TargetUserType,
    bool IncludePins,
    RuleAccessibility Accessible = RuleAccessibility.Yes
);
