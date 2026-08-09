using System.Globalization;
using Mad.Discord;
using NetCord;

namespace Mad.Delete;

public sealed record MessageDeletionOptions(TimeSpan OlderThan, DiscordUserType? Target, bool IncludePins)
{
    /// <summary>Encoded as <c>minutes,target,pins</c>, with <c>-1</c> for "target everyone".</summary>
    public string ToCustomIdArguments() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)OlderThan.TotalMinutes},{(Target is { } target ? (int)target : -1)},{(IncludePins ? 1 : 0)}"
        );

    public static MessageDeletionOptions? FromCustomIdArguments(int minutes, int target, int includePins)
    {
        var olderThan = TimeSpan.FromMinutes(minutes);
        if (!MessageDeletionService.IsValidOlderThan(olderThan))
        {
            return null;
        }

        if (target < -1 || (target >= 0 && !Enum.IsDefined((DiscordUserType)target)))
        {
            return null;
        }

        return new MessageDeletionOptions(olderThan, target < 0 ? null : (DiscordUserType)target, includePins != 0);
    }

    /// <summary>Lower case so callers can drop it into the middle of a sentence.</summary>
    public string Describe(TextGuildChannel channel) =>
        $"anything in {channel} {Describe(OlderThan, Target, IncludePins)}";

    public static string Describe(TimeSpan olderThan, DiscordUserType? target, bool includePins) =>
        $"older than **{MadFormat.Duration(olderThan)}**, from {MadFormat.Target(target)}, {MadFormat.Pins(includePins)}";
}
