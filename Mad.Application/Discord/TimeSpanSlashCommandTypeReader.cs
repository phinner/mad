using System.Globalization;
using System.Text.RegularExpressions;
using NetCord;
using NetCord.Services.ApplicationCommands;

namespace Mad.Discord;

/// <summary>Reads compact durations such as <c>30m</c>, <c>6h</c>, and <c>7d</c>.</summary>
internal sealed partial class TimeSpanSlashCommandTypeReader : SlashCommandTypeReader<ApplicationCommandContext>
{
    public override ApplicationCommandOptionType Type => ApplicationCommandOptionType.String;

    public override ValueTask<SlashCommandTypeReaderResult> ReadAsync(
        string value,
        ApplicationCommandContext context,
        SlashCommandParameter<ApplicationCommandContext> parameter,
        ApplicationCommandServiceConfiguration<ApplicationCommandContext> configuration,
        IServiceProvider? serviceProvider
    ) =>
        new(
            TryRead(value, out var duration)
                ? SlashCommandTypeReaderResult.Success(duration)
                : SlashCommandTypeReaderResult.ParseFail(parameter.Name)
        );

    private static bool TryRead(string value, out TimeSpan duration)
    {
        var match = DurationRegex().Match(value);
        if (!match.Success)
        {
            duration = TimeSpan.Zero;
            return false;
        }

        try
        {
            var years = Read(match, "y");
            var days = checked(years * 365 + Read(match, "d"));
            duration = new TimeSpan(days, Read(match, "h"), Read(match, "m"), Read(match, "s"));
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            duration = TimeSpan.Zero;
            return false;
        }

        static int Read(Match match, string groupName)
        {
            var group = match.Groups[groupName];
            return group.Success ? int.Parse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture) : 0;
        }
    }

    [GeneratedRegex(
        @"^((?<y>\d+)y)?((?<d>\d+)d)?((?<h>\d+)h)?((?<m>\d+)m)?((?<s>\d+)s)?$",
        RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant
    )]
    private static partial Regex DurationRegex();
}
