namespace Mad.Launch;

public sealed class MadConfiguration
{
    public string DiscordToken { get; init; } = "";

    public string DatabasePath { get; init; } = "MadDatabase.sqlite";

    public string? SentryDsn { get; init; }

    public bool Debug { get; init; }

    public ulong? ManagerGuild { get; init; }

    public int MaxChannelsPerGuild { get; init; } = 20;

    public int MaxChannelConcurrency { get; init; } = 10;
}
