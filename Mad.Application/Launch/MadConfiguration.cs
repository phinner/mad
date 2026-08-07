namespace Mad.Launch;

public sealed class MadConfiguration
{
    public required string DiscordToken { get; init; }

    public required string DatabasePath { get; init; } = "MadDatabase.sqlite";

    public bool Debug { get; init; }

    public ulong? ManagerGuild { get; init; }

    public int MaxRulesPerChannel { get; init; } = 1;

    public int MaxRulesPerGuild { get; init; } = 20;
}
