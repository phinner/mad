using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace Mad.Discord;

public abstract class MadApplicationCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    protected Task RespondThemedAsync(IEnumerable<IMessageComponentProperties> components, bool ephemeral = true) =>
        Context.RespondThemedAsync(components, ephemeral);

    protected Task FollowupThemedAsync(IEnumerable<IMessageComponentProperties> components) =>
        FollowupAsync(
            new InteractionMessageProperties
            {
                Components = components,
                Flags = MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral,
            }
        );

    protected async Task DeferThemedAsync()
    {
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
        InteractionTransactions.MarkResponded(Context.Interaction.Id);
    }
}

public abstract class MadComponentInteractionModule : ComponentInteractionModule<ButtonInteractionContext>
{
    protected async Task UpdateThemedAsync(IEnumerable<IMessageComponentProperties> components)
    {
        await RespondAsync(
            InteractionCallback.ModifyMessage(message =>
            {
                message.Components = components;
                message.Flags = MessageFlags.IsComponentsV2;
            })
        );
        InteractionTransactions.MarkResponded(Context.Interaction.Id);
    }
}

internal static class MadInteractionContextExtensions
{
    public static async Task<ulong?> GetGuildIdAsync<TContext>(this TContext context)
        where TContext : IInteractionContext, IGuildContext
    {
        if (context.Guild is { } guild)
        {
            return guild.Id;
        }

        await context.RespondThemedAsync(
            MadTheme.ErrorMessage(
                "I don't do house calls. Run this in the server whose channels you want me working on."
            )
        );
        return null;
    }

    public static async Task<TextGuildChannel?> GetTextChannelAsync<TContext>(this TContext context)
        where TContext : IInteractionContext, IChannelContext
    {
        if (context.Channel is TextGuildChannel channel)
        {
            return channel;
        }

        await context.RespondThemedAsync(
            MadTheme.ErrorMessage("I only work in text channels. Run this in the channel you want me to look after.")
        );
        return null;
    }

    public static async Task RespondThemedAsync(
        this IInteractionContext context,
        IEnumerable<IMessageComponentProperties> components,
        bool ephemeral = true
    )
    {
        await context.Interaction.SendResponseAsync(
            InteractionCallback.Message(
                new InteractionMessageProperties
                {
                    Components = components,
                    Flags = MessageFlags.IsComponentsV2 | (ephemeral ? MessageFlags.Ephemeral : default),
                }
            )
        );
        InteractionTransactions.MarkResponded(context.Interaction.Id);
    }
}
