namespace Mad.Discord;

/// <summary>Turns rule fields into the phrases the modules and the log channel share.</summary>
public static class MadFormat
{
    public static string Duration(TimeSpan duration)
    {
        var parts = new List<string>(3);
        if (duration.Days > 0)
        {
            parts.Add($"{duration.Days}d");
        }
        if (duration.Hours > 0)
        {
            parts.Add($"{duration.Hours}h");
        }
        if (duration.Minutes > 0)
        {
            parts.Add($"{duration.Minutes}m");
        }
        return parts.Count == 0 ? "0m" : string.Join(' ', parts);
    }

    public static string Target(DiscordUserType? target) =>
        target switch
        {
            null => "everyone",
            DiscordUserType.User => "people only",
            DiscordUserType.Bot => "bots only",
            DiscordUserType.Webhook => "webhooks only",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown user type."),
        };

    public static string Pins(bool includePins) => includePins ? "pins deleted too" : "pins kept";
}
