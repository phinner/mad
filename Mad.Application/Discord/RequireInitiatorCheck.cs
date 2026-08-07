using Discord;
using Discord.Interactions;

namespace Mad.Discord;

internal sealed class RequireInitiatorCheck : PreconditionAttribute
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
                PreconditionResult.FromError(
                    "This component is not associated with an interaction response."
                )
            );
        }

        return Task.FromResult(
            originalInteraction.UserId == context.User.Id
                ? PreconditionResult.FromSuccess()
                : PreconditionResult.FromError(
                    "Only the user who initiated this interaction can use this component."
                )
        );
    }
}
