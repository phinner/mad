using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Mad.Discord;

/// <summary>
/// Shared plumbing for M.A.D's modules: guild and text-channel guards, and the Components V2
/// send helpers so no module has to remember the flag.
/// </summary>
public abstract class MadInteractionModule : InteractionModuleBase<SocketInteractionContext>
{
    protected async Task<ulong?> GetGuildIdAsync()
    {
        if (Context.Guild is not null)
        {
            return Context.Guild.Id;
        }

        await RespondThemedAsync(
            MadTheme.ErrorMessage(
                "I don't do house calls. Run this in the server whose channels you want me working on."
            )
        );
        return null;
    }

    protected async Task<ITextChannel?> GetTextChannelAsync()
    {
        if (Context.Channel is ITextChannel channel)
        {
            return channel;
        }

        await RespondThemedAsync(
            MadTheme.ErrorMessage("I only work in text channels. Run this in the channel you want me to look after.")
        );
        return null;
    }

    protected Task RespondThemedAsync(MessageComponent components, bool ephemeral = true) =>
        RespondAsync(ephemeral: ephemeral, components: components, flags: MessageFlags.ComponentsV2);

    protected Task FollowupThemedAsync(MessageComponent components) =>
        FollowupAsync(ephemeral: true, components: components, flags: MessageFlags.ComponentsV2);

    /// <summary>Replaces the message a button lives on.</summary>
    protected Task UpdateThemedAsync(MessageComponent components) =>
        ((SocketMessageComponent)Context.Interaction).UpdateAsync(message =>
        {
            message.Components = components;
            message.Flags = MessageFlags.ComponentsV2;
        });

    protected Task ModifyThemedAsync(MessageComponent components) =>
        ModifyOriginalResponseAsync(message =>
        {
            message.Components = components;
            message.Flags = MessageFlags.ComponentsV2;
        });
}
