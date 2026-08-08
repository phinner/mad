using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

namespace Mad.Discord;

public abstract class MadApplicationCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    protected async Task<ulong?> GetGuildIdAsync()
    {
        if (Context.Guild is { } guild)
        {
            return guild.Id;
        }

        await RespondThemedAsync(
            MadTheme.ErrorMessage(
                "I don't do house calls. Run this in the server whose channels you want me working on."
            )
        );
        return null;
    }

    protected async Task<TextGuildChannel?> GetTextChannelAsync()
    {
        if (Context.Channel is TextGuildChannel channel)
        {
            return channel;
        }

        await RespondThemedAsync(
            MadTheme.ErrorMessage("I only work in text channels. Run this in the channel you want me to look after.")
        );
        return null;
    }

    protected async Task RespondThemedAsync(IEnumerable<IMessageComponentProperties> components, bool ephemeral = true)
    {
        await RespondAsync(InteractionCallback.Message(MadInteractionMessages.Create(components, ephemeral)));
        InteractionTransactions.MarkResponded(Context.Interaction.Id);
    }

    protected Task FollowupThemedAsync(IEnumerable<IMessageComponentProperties> components) =>
        FollowupAsync(MadInteractionMessages.Create(components, ephemeral: true));

    protected async Task DeferThemedAsync()
    {
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
        InteractionTransactions.MarkResponded(Context.Interaction.Id);
    }
}

public abstract class MadComponentInteractionModule : ComponentInteractionModule<ButtonInteractionContext>
{
    protected async Task<ulong?> GetGuildIdAsync()
    {
        if (Context.Guild is { } guild)
        {
            return guild.Id;
        }

        await RespondThemedAsync(
            MadTheme.ErrorMessage(
                "I don't do house calls. Run this in the server whose channels you want me working on."
            )
        );
        return null;
    }

    protected async Task<TextGuildChannel?> GetTextChannelAsync()
    {
        if (Context.Channel is TextGuildChannel channel)
        {
            return channel;
        }

        await RespondThemedAsync(
            MadTheme.ErrorMessage("I only work in text channels. Run this in the channel you want me to look after.")
        );
        return null;
    }

    protected async Task RespondThemedAsync(IEnumerable<IMessageComponentProperties> components, bool ephemeral = true)
    {
        await RespondAsync(InteractionCallback.Message(MadInteractionMessages.Create(components, ephemeral)));
        InteractionTransactions.MarkResponded(Context.Interaction.Id);
    }

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

internal static class MadInteractionMessages
{
    public static InteractionMessageProperties Create(
        IEnumerable<IMessageComponentProperties> components,
        bool ephemeral
    ) =>
        new()
        {
            Components = components,
            Flags = MessageFlags.IsComponentsV2 | (ephemeral ? MessageFlags.Ephemeral : default),
        };
}
