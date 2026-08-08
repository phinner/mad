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

    /// <summary>
    /// Materialised rather than streamed: the caller works through Discord's API for every guild it
    /// gets back, and holding a reader open for that long would pin the connection to the sweep.
    /// </summary>
    public async Task<IReadOnlyList<ulong>> SelectGuildsWithRulesAsync(CancellationToken cancellationToken = default)
    {
        return await db
            .AutoDeleteRules.AsNoTracking()
            .Select(rule => rule.GuildId)
            .Distinct()
            .ToListAsync(cancellationToken);
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
