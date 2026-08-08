using FlexLabs.EntityFrameworkCore.Upsert;
using Mad.Database;
using Microsoft.EntityFrameworkCore;

namespace Mad.Settings;

public sealed class GuildSettingService(MadDbContext db)
{
    public async Task<GuildSetting?> SelectAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        return await db
            .GuildSettings.AsNoTracking()
            .Where(setting => setting.GuildId == guildId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertAsync(GuildSetting setting, CancellationToken cancellationToken = default)
    {
        await db
            .GuildSettings.Upsert(setting)
            .On(existing => existing.GuildId)
            .WhenMatched(
                (existing, incoming) => new GuildSetting(existing.GuildId) { LogChannelId = incoming.LogChannelId }
            )
            .RunAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var deletedRows = await db
            .GuildSettings.Where(setting => setting.GuildId == guildId)
            .ExecuteDeleteAsync(cancellationToken);
        return deletedRows > 0;
    }
}
