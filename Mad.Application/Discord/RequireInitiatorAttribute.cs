using NetCord.Services;
using NetCord.Services.ComponentInteractions;

namespace Mad.Discord;

internal sealed class RequireInitiatorAttribute : PreconditionAttribute<ButtonInteractionContext>
{
    public override ValueTask<PreconditionResult> EnsureCanExecuteAsync(
        ButtonInteractionContext context,
        IServiceProvider? serviceProvider
    )
    {
        if (context.Message.InteractionMetadata is not { } originalInteraction)
        {
            return new(PreconditionResult.Fail("I can't tell which command these buttons belong to. Run it again."));
        }

        return new(
            originalInteraction.User.Id == context.User.Id
                ? PreconditionResult.Success
                : PreconditionResult.Fail(
                    "These buttons belong to whoever ran the command. Run it yourself and you'll get your own."
                )
        );
    }
}
