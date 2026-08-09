using System.Numerics;
using NetCord;
using NetCord.Rest;

namespace Mad.Discord;

public static class MadPermissions
{
    public const Permissions AutoDelete =
        Permissions.ViewChannel | Permissions.ReadMessageHistory | Permissions.ManageMessages;

    public const Permissions Log = Permissions.ViewChannel | Permissions.SendMessages;

    private static readonly (Permissions Flag, string Name)[] Names =
    [
        (Permissions.ViewChannel, "View Channel"),
        (Permissions.ReadMessageHistory, "Read Message History"),
        (Permissions.ManageMessages, "Manage Messages"),
        (Permissions.SendMessages, "Send Messages"),
    ];

    public static string Describe(Permissions permissions)
    {
        var names = Names.Where(name => permissions.HasFlag(name.Flag)).Select(name => $"**{name.Name}**").ToArray();
        return names.Length switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => $"{string.Join(", ", names[..^1])} and {names[^1]}",
        };
    }

    public static IEnumerable<IMessageComponentProperties>? MissingMessage(
        Permissions granted,
        Permissions required,
        TextGuildChannel channel,
        string purpose
    )
    {
        var missing = required & ~granted;
        if (missing is default(Permissions))
        {
            return null;
        }

        var grant = BitOperations.PopCount((ulong)missing) == 1 ? "Grant that" : "Grant those";
        return MadTheme.ErrorMessage(
            $"I need {Describe(missing)} in {channel} before I can {purpose}. {grant} and ask me again."
        );
    }
}
