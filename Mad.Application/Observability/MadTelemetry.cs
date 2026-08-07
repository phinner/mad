using System.Diagnostics.Metrics;

namespace Mad.Observability;

internal static class MadTelemetry
{
    private const string MeterName = "Mad";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> ScannedMessages = Meter.CreateCounter<long>(
        "mad.deletion.messages.scanned",
        unit: "{message}",
        description: "Messages evaluated by the deletion job."
    );

    public static readonly Counter<long> DeletedMessages = Meter.CreateCounter<long>(
        "mad.deletion.messages.deleted",
        unit: "{message}",
        description: "Messages deleted by the deletion job."
    );

    public static readonly Counter<long> ScannedGuilds = Meter.CreateCounter<long>(
        "mad.deletion.guilds.scanned",
        unit: "{guild}",
        description: "Guilds with rules visited by the deletion job."
    );

    public static readonly Counter<long> ScannedChannels = Meter.CreateCounter<long>(
        "mad.deletion.channels.scanned",
        unit: "{channel}",
        description: "Channels with rules visited by the deletion job."
    );

    public static readonly Counter<long> StaleGuilds = Meter.CreateCounter<long>(
        "mad.deletion.guilds.stale",
        unit: "{guild}",
        description: "Guilds with rules that no longer exist; their rules were removed."
    );

    public static readonly Counter<long> StaleChannels = Meter.CreateCounter<long>(
        "mad.deletion.channels.stale",
        unit: "{channel}",
        description: "Channels with rules that no longer exist; their rules were removed."
    );
}
