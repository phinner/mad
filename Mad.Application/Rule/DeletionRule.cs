using System.ComponentModel.DataAnnotations;
using Mad.Discord;
using Microsoft.EntityFrameworkCore;

namespace Mad.Rule;

[PrimaryKey(nameof(GuildId), nameof(Name))]
public sealed record DeletionRule(
    ulong GuildId,
    [property:
        MinLength(DeletionRuleService.MinNameLength),
        MaxLength(DeletionRuleService.MaxNameLength)
    ]
        string Name,
    ulong ChannelId,
    DiscordUserType UserType,
    TimeSpan OlderThan
);
