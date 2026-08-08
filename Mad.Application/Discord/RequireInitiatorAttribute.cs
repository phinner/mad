using Discord;
using Discord.Interactions;

namespace Mad.Discord;

internal sealed class RequireInitiatorAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context,
        ICommandInfo commandInfo,
        IServiceProvider services
    )
    {
        if (
            context.Interaction is not IComponentInteraction component
            || component.Message.InteractionMetadata is not { } originalInteraction
        )
        {
            return Task.FromResult(
                PreconditionResult.FromError("I can't tell which command these buttons belong to. Run it again.")
            );
        }

        return Task.FromResult(
            originalInteraction.UserId == context.User.Id
                ? PreconditionResult.FromSuccess()
                : PreconditionResult.FromError(
                    "These buttons belong to whoever ran the command. Run it yourself and you'll get your own."
                )
        );
    }
}
