using System.Data;
using Mad.Database;
using Mad.Discord;
using Mad.Launch;
using Microsoft.EntityFrameworkCore;

namespace Mad.Delete;

public sealed class AutoDeleteRuleService(MadDbContext db, MadConfiguration configuration)
{
    public async Task<Result> InsertAsync(
        ulong guildId,
        ulong channelId,
        TimeSpan olderThan,
        DiscordUserType? targetUserType,
        bool includePins,
        CancellationToken cancellationToken = default
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken
        );

        var alreadyExists = await db.AutoDeleteRules.AnyAsync(
            rule => rule.GuildId == guildId && rule.ChannelId == channelId,
            cancellationToken
        );
        if (alreadyExists)
        {
            return new Result.AlreadyExists();
        }

        var configuredChannelCount = await db.AutoDeleteRules.CountAsync(
            rule => rule.GuildId == guildId,
            cancellationToken
        );
        if (configuredChannelCount >= configuration.MaxChannelsPerGuild)
        {
            return new Result.GuildLimit(configuration.MaxChannelsPerGuild);
        }

        await db.AutoDeleteRules.AddAsync(
            new AutoDeleteRule(guildId, channelId, olderThan, targetUserType, includePins),
            cancellationToken
        );
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new Result.Created();
    }

    public async Task<IReadOnlyList<AutoDeleteRule>> SelectByGuildAsync(
        ulong guildId,
        CancellationToken cancellationToken = default
    )
    {
        return await db
            .AutoDeleteRules.AsNoTracking()
            .Where(rule => rule.GuildId == guildId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AutoDeleteRule>> SelectAllAsync(
        int limit,
        AutoDeleteRule? cursor = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        // SQLite cannot translate ordering comparisons for ulong. Keep the keyset query in SQL;
        // Discord snowflakes are stored as INTEGER and the composite primary key supplies the order.

        // TODO I have the feeling the clanker is hallucinating... but meh, we love raw SQL...
        var query = cursor is null
            ? db.AutoDeleteRules.FromSqlInterpolated(
                $"""
                SELECT "GuildId", "ChannelId", "OlderThan", "TargetUserType", "IncludePins"
                FROM "AutoDeleteRules"
                ORDER BY "GuildId", "ChannelId"
                LIMIT {limit}
                """
            )
            : db.AutoDeleteRules.FromSqlInterpolated(
                $"""
                SELECT "GuildId", "ChannelId", "OlderThan", "TargetUserType", "IncludePins"
                FROM "AutoDeleteRules"
                WHERE "GuildId" > {cursor.GuildId}
                   OR ("GuildId" = {cursor.GuildId} AND "ChannelId" > {cursor.ChannelId})
                ORDER BY "GuildId", "ChannelId"
                LIMIT {limit}
                """
            );

        return await query.AsNoTracking().ToListAsync(cancellationToken);
    }

    public Task<int> DeleteByGuildAndChannelAsync(
        ulong guildId,
        ulong channelId,
        CancellationToken cancellationToken = default
    ) =>
        db
            .AutoDeleteRules.Where(rule => rule.GuildId == guildId && rule.ChannelId == channelId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<int> DeleteByGuildAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        db.AutoDeleteRules.Where(rule => rule.GuildId == guildId).ExecuteDeleteAsync(cancellationToken);

    public abstract record Result
    {
        private Result() { }

        public sealed record Created : Result;

        public sealed record AlreadyExists : Result;

        public sealed record GuildLimit(int Value) : Result;
    }
}
