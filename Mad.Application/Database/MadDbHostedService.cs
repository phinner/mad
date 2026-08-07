using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mad.Database;

internal sealed class MadDbHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MadDbContext>().Database;

        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await database.EnsureCreatedAsync(cancellationToken);
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
