using Mad.Delete;
using Mad.Settings;
using Microsoft.EntityFrameworkCore;

namespace Mad.Database;

public sealed class MadDbContext(DbContextOptions<MadDbContext> options) : DbContext(options)
{
    public DbSet<AutoDeleteRule> AutoDeleteRules => Set<AutoDeleteRule>();

    public DbSet<GuildSettings> GuildSettings => Set<GuildSettings>();
}
