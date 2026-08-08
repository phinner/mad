using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mad.Database;

/// <summary>Lets `dotnet ef` create the context without running the host.</summary>
internal sealed class MadDbContextFactory : IDesignTimeDbContextFactory<MadDbContext>
{
    public MadDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MadDbContext>().UseSqlite("Data Source=MadDatabase.sqlite").Options);
}
