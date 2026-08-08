// See https://aka.ms/new-console-template for more information

using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Mad.Database;
using Mad.Discord;
using Mad.Rule;

namespace Mad.Launch;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal static class MadProgram
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile("MadConfig.json", optional: true);
        builder.Configuration.AddEnvironmentVariables(prefix: "MAD_");

        var configuration =
            builder.Configuration.Get<MadConfiguration>()
            ?? throw new InvalidOperationException("Mad configuration is required.");

        if (string.IsNullOrWhiteSpace(configuration.DiscordToken))
        {
            throw new InvalidOperationException("DiscordToken must be configured.");
        }

        if (string.IsNullOrWhiteSpace(configuration.DatabasePath))
        {
            throw new InvalidOperationException("DatabasePath must be configured.");
        }

        if (configuration is { Debug: true, ManagerGuild: not > 0 })
        {
            throw new InvalidOperationException(
                "ManagerGuild must be configured when Debug is enabled."
            );
        }

        if (configuration.MaxRulesPerChannel <= 0)
        {
            throw new InvalidOperationException("MaxRulesPerChannel must be greater than zero.");
        }

        if (configuration.MaxRulesPerGuild <= 0)
        {
            throw new InvalidOperationException("MaxRulesPerGuild must be greater than zero.");
        }

        if (!string.IsNullOrWhiteSpace(configuration.SentryDsn))
        {
            builder.Logging.AddSentry(options =>
            {
                options.Dsn = configuration.SentryDsn;
                options.Debug = configuration.Debug;
                options.EnableLogs = true;
                options.EnableMetrics = true;
                options.TracesSampleRate = 1.0;
            });
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = configuration.DatabasePath,
        };

        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<DiscordSocketClient>();
        builder.Services.AddSingleton(serviceProvider => new InteractionService(
            serviceProvider.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig
            {
                // Sync so ExecuteCommandAsync surfaces the real result; DiscordHostedService
                // offloads each interaction to the thread pool to keep the gateway task free.
                DefaultRunMode = RunMode.Sync,
                AutoServiceScopes = true,
            }
        ));
        builder.Services.AddSingleton(
            new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages,
            }
        );

        builder.Services.AddDbContext<MadDbContext>(options =>
            options.UseSqlite(connectionString.ConnectionString)
        );
        builder.Services.AddScoped<DeletionRuleService>();

        builder.Services.AddHostedService<MadDbHostedService>();
        builder.Services.AddHostedService<DeletionRuleHostedService>();
        builder.Services.AddHostedService<DiscordHostedService>();

        await builder.Build().RunAsync();
    }
}
