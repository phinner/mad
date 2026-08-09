using Mad.Database;
using Microsoft.EntityFrameworkCore;

namespace Mad.Settings;

public sealed class GuildSettingsService(MadDbContext db)
{
    public async Task<GuildSettings?> SelectAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        return await db
            .GuildSettings.AsNoTracking()
            .Where(setting => setting.GuildId == guildId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertAsync(GuildSettings settings, CancellationToken cancellationToken = default)
    {
        await db
            .GuildSettings.Upsert(settings)
            .On(existing => existing.GuildId)
            .WhenMatched(
                (existing, incoming) => new GuildSettings(existing.GuildId) { LogChannelId = incoming.LogChannelId }
            )
            .RunAsync(cancellationToken);
    }

    public async Task DeleteAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        await db.GuildSettings.Where(setting => setting.GuildId == guildId).ExecuteDeleteAsync(cancellationToken);
    }
}
