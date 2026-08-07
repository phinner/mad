using Mad.Rule;
using Microsoft.EntityFrameworkCore;

namespace Mad.Database;

public sealed class MadDbContext(DbContextOptions<MadDbContext> options) : DbContext(options)
{
    public DbSet<DeletionRule> DeletionRules => Set<DeletionRule>();
}
