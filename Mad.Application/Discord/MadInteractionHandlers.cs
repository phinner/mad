using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using Sentry;

namespace Mad.Discord;

internal sealed class MadApplicationCommandPreExecutionHandler
    : IApplicationCommandPreExecutionHandler<ApplicationCommandContext>
{
    public ValueTask<PreExecutionResult> HandleAsync(
        ApplicationCommandContext context,
        GatewayClient? client,
        ILogger logger,
        IServiceProvider services
    )
    {
        InteractionTransactions.Start(context.Interaction, GetTransactionName(context.Interaction));
        logger.LogInformation(
            "Received Discord application command {InteractionId} named {CommandName}.",
            context.Interaction.Id,
            context.Interaction.Data.Name
        );
        return new(PreExecutionResult.Continue);
    }

    private static string GetTransactionName(ApplicationCommandInteraction interaction)
    {
        var name = $"/{interaction.Data.Name}";
        if (interaction is not SlashCommandInteraction slash)
        {
            return name;
        }

        var options = slash.Data.Options;
        while (
            options.FirstOrDefault(option =>
                option.Type is ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup
            )
                is { } subCommand
        )
        {
            name += $" {subCommand.Name}";
            options = subCommand.Options ?? [];
        }

        return name;
    }
}

internal sealed class MadComponentInteractionPreExecutionHandler
    : IComponentInteractionPreExecutionHandler<ButtonInteractionContext>
{
    public ValueTask<PreExecutionResult> HandleAsync(
        ButtonInteractionContext context,
        GatewayClient? client,
        ILogger logger,
        IServiceProvider services
    )
    {
        var customId = context.Interaction.Data.CustomId;
        InteractionTransactions.Start(context.Interaction, $"component {customId}");
        logger.LogInformation(
            "Received Discord component interaction {InteractionId} with custom ID {CustomId}.",
            context.Interaction.Id,
            customId
        );
        return new(PreExecutionResult.Continue);
    }
}

internal sealed class MadApplicationCommandResultHandler : IApplicationCommandResultHandler<ApplicationCommandContext>
{
    public ValueTask HandleResultAsync(
        IExecutionResult result,
        ApplicationCommandContext context,
        GatewayClient? client,
        ILogger logger,
        IServiceProvider services
    ) => InteractionResults.HandleAsync(result, context.Interaction, logger);
}

internal sealed class MadComponentInteractionResultHandler
    : IComponentInteractionResultHandler<ButtonInteractionContext>
{
    public ValueTask HandleResultAsync(
        IExecutionResult result,
        ButtonInteractionContext context,
        GatewayClient? client,
        ILogger logger,
        IServiceProvider services
    ) => InteractionResults.HandleAsync(result, context.Interaction, logger);
}

internal static class InteractionResults
{
    private const string GenericFailure =
        "Something went wrong on my end, so I've not touched anything. Give it another go in a moment, "
        + "and report it on GitHub if it keeps happening.";

    public static async ValueTask HandleAsync(IExecutionResult result, Interaction interaction, ILogger logger)
    {
        try
        {
            if (result is not IFailResult failure)
            {
                InteractionTransactions.Finish(interaction.Id, SpanStatus.Ok);
                return;
            }

            var status = failure is IExceptionResult ? SpanStatus.InternalError : SpanStatus.UnknownError;
            var responded = InteractionTransactions.Finish(interaction.Id, status);

            if (failure is IExceptionResult exceptionResult)
            {
                logger.LogError(
                    exceptionResult.Exception,
                    "Discord interaction {InteractionId} threw.",
                    interaction.Id
                );
            }
            else
            {
                logger.LogWarning(
                    "Discord interaction {InteractionId} failed: {Error}",
                    interaction.Id,
                    failure.Message
                );
            }

            var body = failure is PreconditionFailResult ? failure.Message : GenericFailure;
            var message = MadInteractionMessages.Create(MadTheme.ErrorMessage(body), ephemeral: true);
            if (responded)
            {
                await interaction.SendFollowupMessageAsync(message);
            }
            else
            {
                await interaction.SendResponseAsync(InteractionCallback.Message(message));
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not handle the result for interaction {InteractionId}.", interaction.Id);
        }
    }
}

internal static class InteractionTransactions
{
    private static readonly ConcurrentDictionary<ulong, TransactionState> States = new();

    public static void Start(Interaction interaction, string name)
    {
        var scope = SentrySdk.PushScope();
        var transaction = SentrySdk.StartTransaction(name, "discord.interaction");
        transaction.SetTag("discord.interaction_type", interaction.GetType().Name);
        if (interaction.GuildId is { } guildId)
        {
            transaction.SetTag("discord.guild_id", guildId.ToString());
        }
        transaction.SetTag("discord.channel_id", interaction.Channel.Id.ToString());
        SentrySdk.ConfigureScope(current => current.Transaction = transaction);

        if (!States.TryAdd(interaction.Id, new(scope, transaction)))
        {
            transaction.Finish(SpanStatus.AlreadyExists);
            scope.Dispose();
        }
    }

    public static void MarkResponded(ulong interactionId)
    {
        if (States.TryGetValue(interactionId, out var state))
        {
            state.Responded = true;
        }
    }

    public static bool Finish(ulong interactionId, SpanStatus status)
    {
        if (!States.TryRemove(interactionId, out var state))
        {
            return false;
        }

        state.Transaction.Finish(status);
        state.Scope.Dispose();
        return state.Responded;
    }

    private sealed class TransactionState(IDisposable scope, ITransactionTracer transaction)
    {
        public IDisposable Scope { get; } = scope;
        public ITransactionTracer Transaction { get; } = transaction;
        public volatile bool Responded;
    }
}
