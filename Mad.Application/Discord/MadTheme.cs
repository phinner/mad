using NetCord;
using NetCord.Rest;

namespace Mad.Discord;

public static class MadTheme
{
    public static readonly Color Info = new(0xFFA23C);
    public static readonly Color Success = new(0xFFC940);
    public static readonly Color Error = new(0xC0562F);

    public static IEnumerable<IMessageComponentProperties> InfoMessage(
        string body,
        params ButtonProperties[] buttons
    ) => Message(Info, body, buttons);

    public static IEnumerable<IMessageComponentProperties> SuccessMessage(
        string body,
        params ButtonProperties[] buttons
    ) => Message(Success, body, buttons);

    public static IEnumerable<IMessageComponentProperties> ErrorMessage(
        string body,
        params ButtonProperties[] buttons
    ) => Message(Error, body, buttons);

    private static IEnumerable<IMessageComponentProperties> Message(
        Color accent,
        string body,
        params ButtonProperties[] buttons
    )
    {
        var components = new List<IComponentContainerComponentProperties> { new TextDisplayProperties(body) };
        if (buttons.Length > 0)
        {
            components.Add(new ActionRowProperties(buttons));
        }

        return Message(accent, components);
    }

    public static IEnumerable<IMessageComponentProperties> Message(
        Color accent,
        IEnumerable<IComponentContainerComponentProperties> components
    ) => [new ComponentContainerProperties(components) { AccentColor = accent }];

    public static ButtonProperties ConfirmButton(string customId) => new(customId, "Confirm", ButtonStyle.Success);

    public static ButtonProperties CancelButton(string customId) => new(customId, "Cancel", ButtonStyle.Secondary);

    public static ButtonProperties PageButton(string label, string customId, bool disabled) =>
        new(customId, label, ButtonStyle.Secondary) { Disabled = disabled };
}
