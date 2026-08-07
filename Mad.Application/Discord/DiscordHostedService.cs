using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Mad.Launch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentry;

namespace Mad.Discord;

internal sealed class DiscordHostedService(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    MadConfiguration configuration,
    ILogger<DiscordHostedService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += LogAsync;
        interactions.Log += LogAsync;
        client.InteractionCreated += OnInteractionCreatedAsync;
        await interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), services);

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client.Ready += SignalReadyAsync;
        try
        {
            await client.LoginAsync(TokenType.Bot, configuration.DiscordToken);
            await client.StartAsync();
            await ready.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            client.Ready -= SignalReadyAsync;
        }

        if (configuration.Debug)
        {
            await interactions.RegisterCommandsToGuildAsync(configuration.ManagerGuild!.Value);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Discord commands registered to manager guild {GuildId}.",
                    configuration.ManagerGuild
                );
            }
        }
        else
        {
            await interactions.RegisterCommandsGloballyAsync();
            logger.LogInformation("Discord commands registered globally.");
        }
        logger.LogInformation("Discord client started.");
        return;

        Task SignalReadyAsync()
        {
            ready.TrySetResult();
            return Task.CompletedTask;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping Discord client.");
        client.InteractionCreated -= OnInteractionCreatedAsync;
        client.Log -= LogAsync;
        interactions.Log -= LogAsync;
        await client.StopAsync();
        await client.LogoutAsync();
    }

    private Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        // Commands run with RunMode.Sync; hop off the gateway task so command
        // execution (database and REST calls) never blocks heartbeats.
        _ = Task.Run(() => ExecuteInteractionAsync(interaction));
        return Task.CompletedTask;
    }

    private async Task ExecuteInteractionAsync(SocketInteraction interaction)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Received Discord interaction {InteractionId} of type {InteractionType}.",
                interaction.Id,
                interaction.Type
            );
        }

        var transaction = SentrySdk.StartTransaction(
            GetTransactionName(interaction),
            "discord.interaction"
        );
        transaction.SetTag("discord.interaction_type", interaction.Type.ToString());
        if (interaction.GuildId is { } guildId)
        {
            transaction.SetTag("discord.guild_id", guildId.ToString());
        }
        if (interaction.ChannelId is { } channelId)
        {
            transaction.SetTag("discord.channel_id", channelId.ToString());
        }
        SentrySdk.ConfigureScope(scope => scope.Transaction = transaction);

        try
        {
            var context = new SocketInteractionContext(client, interaction);
            var result = await interactions.ExecuteCommandAsync(context, services);

            if (result.IsSuccess)
            {
                transaction.Status = SpanStatus.Ok;
                return;
            }

            transaction.Status = SpanStatus.UnknownError;
            logger.LogWarning(
                "Interaction {InteractionId} failed: {Error}",
                interaction.Id,
                result.ErrorReason
            );

            await SendFailureResponseAsync(interaction);
        }
        catch (Exception exception)
        {
            transaction.Status = SpanStatus.InternalError;
            logger.LogError(exception, "Interaction {InteractionId} threw.", interaction.Id);
            await SendFailureResponseAsync(interaction);
        }
        finally
        {
            transaction.Finish();
        }
    }

    private static string GetTransactionName(SocketInteraction interaction) =>
        interaction switch
        {
            SocketSlashCommand slash => GetSlashCommandName(slash),
            SocketCommandBase command => $"/{command.CommandName}",
            SocketMessageComponent component => $"component {component.Data.CustomId}",
            SocketModal modal => $"modal {modal.Data.CustomId}",
            _ => interaction.Type.ToString(),
        };

    private static string GetSlashCommandName(SocketSlashCommand slash)
    {
        var name = $"/{slash.CommandName}";
        var options = slash.Data.Options;
        while (
            options?.FirstOrDefault(option =>
                option.Type
                    is ApplicationCommandOptionType.SubCommand
                        or ApplicationCommandOptionType.SubCommandGroup
            )
                is { } subCommand
        )
        {
            name += $" {subCommand.Name}";
            options = subCommand.Options;
        }
        return name;
    }

    private async Task SendFailureResponseAsync(IDiscordInteraction interaction)
    {
        try
        {
            if (interaction.HasResponded)
            {
                await interaction.FollowupAsync(
                    "This interaction could not be completed.",
                    ephemeral: true
                );
            }
            else
            {
                await interaction.RespondAsync(
                    "This interaction could not be completed.",
                    ephemeral: true
                );
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not send the failure response for interaction {InteractionId}.",
                interaction.Id
            );
        }
    }

    private Task LogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.Severity, null),
        };
        if (logger.IsEnabled(level))
        {
            logger.Log(level, message.Exception, "Discord.Net: {Message}", message.Message);
        }
        return Task.CompletedTask;
    }
}
