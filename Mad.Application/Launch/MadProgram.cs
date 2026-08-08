using System.Reflection;
using Mad.Database;
using Mad.Delete;
using Mad.Discord;
using Mad.Log;
using Mad.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace Mad.Launch;

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

        if (configuration is { Debug: true, ManagerGuild: null or 0 })
        {
            throw new InvalidOperationException("ManagerGuild must be configured when Debug is enabled.");
        }

        if (configuration.MaxChannelsPerGuild <= 0)
        {
            throw new InvalidOperationException("MaxChannelsPerGuild must be greater than zero.");
        }

        if (configuration.MaxChannelConcurrency <= 0)
        {
            throw new InvalidOperationException("MaxChannelConcurrency must be greater than zero.");
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

        var connectionString = new SqliteConnectionStringBuilder { DataSource = configuration.DatabasePath };

        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<CommandMentions>();

        builder.Services.AddDbContext<MadDbContext>(options => options.UseSqlite(connectionString.ConnectionString));
        builder.Services.AddScoped<AutoDeleteRuleService>();
        builder.Services.AddScoped<GuildSettingService>();
        builder.Services.AddSingleton<LogNotifier>();

        // These services subscribe to gateway events before NetCord starts the connection.
        builder.Services.AddHostedService<MadDbHostedService>();
        builder.Services.AddHostedService<AutoDeleteHostedService>();
        builder.Services.AddHostedService<DiscordHostedService>();

        builder.Services.AddDiscordGateway(options =>
        {
            options.Token = configuration.DiscordToken;
            options.Intents = GatewayIntents.Guilds | GatewayIntents.GuildMessages;
        });
        builder.Services.AddApplicationCommands(options =>
        {
            options.AutoRegisterCommands = false;
            options.TypeReaders[typeof(TimeSpan)] = new TimeSpanSlashCommandTypeReader();
            options.PreExecutionHandler = new MadApplicationCommandPreExecutionHandler();
            options.ResultHandler = new MadApplicationCommandResultHandler();
        });
        builder.Services.AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>(options =>
        {
            options.ParameterSeparator = ',';
            options.PreExecutionHandler = new MadComponentInteractionPreExecutionHandler();
            options.ResultHandler = new MadComponentInteractionResultHandler();
        });

        var host = builder.Build();
        host.AddModules(Assembly.GetExecutingAssembly());

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        try
        {
            await host.RunAsync();
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // Ctrl+C can arrive while a hosted service is still starting. In that case the
            // generic host reports startup cancellation through RunAsync even though the
            // application is already performing a requested, graceful shutdown.
        }
    }
}
