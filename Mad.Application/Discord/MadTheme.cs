using Discord;

namespace Mad.Discord;

/// <summary>
/// Builds every message M.A.D sends. Components V2 only: a coloured container holding the body
/// text and, when there is something to decide, a row of buttons. Components V2 forbids
/// <c>Content</c> and embeds, so senders must pass <see cref="MessageFlags.ComponentsV2"/>.
/// </summary>
public static class MadTheme
{
    public static readonly Color Info = new(0xFFA23C);
    public static readonly Color Success = new(0xFFC940);
    public static readonly Color Error = new(0xC0562F);

    public static MessageComponent InfoMessage(string body, params ButtonBuilder[] buttons) =>
        Message(Info, body, buttons);

    public static MessageComponent SuccessMessage(string body, params ButtonBuilder[] buttons) =>
        Message(Success, body, buttons);

    public static MessageComponent ErrorMessage(string body, params ButtonBuilder[] buttons) =>
        Message(Error, body, buttons);

    private static MessageComponent Message(Color accent, string body, params ButtonBuilder[] buttons)
    {
        var components = new List<IMessageComponentBuilder> { new TextDisplayBuilder(body) };
        if (buttons.Length <= 0)
        {
            return Message(accent, components);
        }

        var actions = new ActionRowBuilder();
        foreach (var button in buttons)
        {
            actions.WithButton(button);
        }
        components.Add(actions);

        return Message(accent, components);
    }

    public static MessageComponent Message(Color accent, IEnumerable<IMessageComponentBuilder> components) =>
        new ComponentBuilderV2()
            .AddComponent(new ContainerBuilder().WithAccentColor(accent).WithComponents(components))
            .Build();

    public static ButtonBuilder ConfirmButton(string customId) => new("Confirm", customId, ButtonStyle.Success);

    public static ButtonBuilder CancelButton(string customId) => new("Cancel", customId, ButtonStyle.Secondary);

    public static ButtonBuilder PageButton(string label, string customId, bool disabled) =>
        new(label, customId, ButtonStyle.Secondary, isDisabled: disabled);
}
