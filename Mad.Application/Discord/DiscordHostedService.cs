using Mad.Launch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Mad.Discord;

internal sealed class DiscordHostedService(
    GatewayClient client,
    RestClient rest,
    ApplicationCommandService<ApplicationCommandContext> commands,
    CommandMentions commandMentions,
    MadConfiguration configuration,
    ILogger<DiscordHostedService> logger
) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += RegisterCommandsAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.Ready -= RegisterCommandsAsync;
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose() => _stopping.Dispose();

    private async ValueTask RegisterCommandsAsync(ReadyEventArgs ready)
    {
        try
        {
            IReadOnlyList<ApplicationCommand> registered;
            if (configuration.Debug)
            {
                var manager = configuration.ManagerGuild!.Value;
                await rest.BulkOverwriteGlobalApplicationCommandsAsync(
                    ready.ApplicationId,
                    [],
                    cancellationToken: _stopping.Token
                );
                logger.LogInformation("Cleared global commands for debug mode.");

                registered = await commands.RegisterCommandsAsync(
                    rest,
                    ready.ApplicationId,
                    manager,
                    cancellationToken: _stopping.Token
                );
                logger.LogInformation("Discord commands registered to manager guild {GuildId}.", manager);
            }
            else
            {
                registered = await commands.RegisterCommandsAsync(
                    rest,
                    ready.ApplicationId,
                    cancellationToken: _stopping.Token
                );
                logger.LogInformation("Discord commands registered globally.");

                if (configuration.ManagerGuild is { } manager)
                {
                    await rest.BulkOverwriteGuildApplicationCommandsAsync(
                        ready.ApplicationId,
                        manager,
                        [],
                        cancellationToken: _stopping.Token
                    );
                    logger.LogInformation("Cleared commands from manager guild {GuildId}.", manager);
                }
            }

            commandMentions.Replace(registered);
            logger.LogInformation("Discord client is ready.");
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not register Discord commands.");
        }
    }
}
