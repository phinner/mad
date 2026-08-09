using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Mad.Settings;

[PrimaryKey(nameof(GuildId))]
public sealed record GuildSettings(
    [property: DatabaseGenerated(DatabaseGeneratedOption.None)] ulong GuildId,
    ulong? LogChannelId = null
);
