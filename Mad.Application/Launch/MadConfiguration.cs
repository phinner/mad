namespace Mad.Launch;

public sealed class MadConfiguration
{
    public string DiscordToken { get; init; } = "";

    public string DatabasePath { get; init; } = "MadDatabase.sqlite";

    public string? SentryDsn { get; init; }

    public bool Debug { get; init; }

    public ulong? ManagerGuild { get; init; }

    /// <summary>How many channels one guild may have automatic deletion enabled on.</summary>
    public int MaxChannelsPerGuild { get; init; } = 20;

    /// <summary>How many of a guild's channels a single sweep may work through at once.</summary>
    public int MaxChannelConcurrency { get; init; } = 10;
}
