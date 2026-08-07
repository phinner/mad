using System.Data;
using System.Runtime.CompilerServices;
using Mad.Database;
using Mad.Discord;
using Mad.Launch;
using Microsoft.EntityFrameworkCore;

namespace Mad.Rule;

public sealed class DeletionRuleService(MadDbContext db, MadConfiguration configuration)
{
    public const int MinNameLength = 3;
    public const int MaxNameLength = 64;

    public async Task<Result> CreateAsync(
        ulong guildId,
        string name,
        ulong channelId,
        DiscordUserType userType,
        TimeSpan olderThan,
        CancellationToken cancellation = default
    )
    {
        name = NormalizeName(name);
        if (name.Length is < MinNameLength or > MaxNameLength)
        {
            return new Result.InvalidName(MinNameLength, MaxNameLength);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellation
        );

        var existingRules = await db
            .DeletionRules.AsNoTracking()
            .Where(rule => rule.GuildId == guildId)
            .Select(rule => new { rule.Name, rule.ChannelId })
            .ToListAsync(cancellation);

        if (existingRules.Any(rule => rule.Name == name))
        {
            return new Result.DuplicateName();
        }

        if (
            existingRules.Count(rule => rule.ChannelId == channelId)
            >= configuration.MaxRulesPerChannel
        )
        {
            return new Result.ChannelLimit(configuration.MaxRulesPerChannel);
        }

        if (existingRules.Count >= configuration.MaxRulesPerGuild)
        {
            return new Result.GuildLimit(configuration.MaxRulesPerGuild);
        }

        await db.DeletionRules.AddAsync(
            new DeletionRule(guildId, name, channelId, userType, olderThan),
            cancellation
        );
        await db.SaveChangesAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return new Result.Success();
    }

    public async Task<IReadOnlyList<DeletionRule>> SelectByGuildAsync(
        ulong guildId,
        CancellationToken cancellation = default
    )
    {
        return await db
            .DeletionRules.AsNoTracking()
            .Where(rule => rule.GuildId == guildId)
            .ToListAsync(cancellation);
    }

    public async IAsyncEnumerable<ulong> SelectGuildsWithRulesAsync(
        [EnumeratorCancellation] CancellationToken cancellation = default
    )
    {
        var guilds = db
            .DeletionRules.AsNoTracking()
            .Select(rule => rule.GuildId)
            .Distinct()
            .AsAsyncEnumerable();

        await foreach (var guild in guilds.WithCancellation(cancellation))
        {
            yield return guild;
        }
    }

    public async IAsyncEnumerable<ulong> SelectChannelsWithRulesAsync(
        ulong guildId,
        [EnumeratorCancellation] CancellationToken cancellation = default
    )
    {
        var channels = db
            .DeletionRules.AsNoTracking()
            .Where(rule => rule.GuildId == guildId)
            .Select(rule => rule.ChannelId)
            .Distinct()
            .AsAsyncEnumerable();

        await foreach (var channel in channels.WithCancellation(cancellation))
        {
            yield return channel;
        }
    }

    public async Task<IReadOnlyList<DeletionRule>> SelectByGuildAndChannelAsync(
        ulong guildId,
        ulong channelId,
        CancellationToken cancellation = default
    )
    {
        return await db
            .DeletionRules.AsNoTracking()
            .Where(rule => rule.GuildId == guildId && rule.ChannelId == channelId)
            .ToListAsync(cancellation);
    }

    public Task<int> DeleteByGuildAndChannelAsync(
        ulong guildId,
        ulong channelId,
        CancellationToken cancellation = default
    ) =>
        db
            .DeletionRules.Where(rule => rule.GuildId == guildId && rule.ChannelId == channelId)
            .ExecuteDeleteAsync(cancellation);

    public Task<int> DeleteByGuildAsync(ulong guildId, CancellationToken cancellation = default) =>
        db.DeletionRules.Where(rule => rule.GuildId == guildId).ExecuteDeleteAsync(cancellation);

    public async Task<bool> DeleteAsync(
        ulong guildId,
        string name,
        CancellationToken cancellation = default
    )
    {
        name = NormalizeName(name);
        var deletedRows = await db
            .DeletionRules.Where(rule => rule.GuildId == guildId && rule.Name == name)
            .ExecuteDeleteAsync(cancellation);
        return deletedRows > 0;
    }

    public static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

    public abstract record Result
    {
        private Result() { }

        public sealed record Success : Result;

        public sealed record InvalidName(int MinLength, int MaxLength) : Result;

        public sealed record ChannelLimit(int Value) : Result;

        public sealed record GuildLimit(int Value) : Result;

        public sealed record DuplicateName : Result;
    }
}
