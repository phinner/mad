# M.A.D

Message Auto Delete, a small Discord bot that automatically deletes messages with user defined rules.

## Installation

[Add M.A.D to your Discord server](https://discord.com/oauth2/authorize?client_id=1534942157306073259).

## Requirements

- .NET 10 SDK
- A Discord bot token
- `View Channel`, `Read Message History`, and `Manage Messages` permissions in managed channels

## Configuration

Mad reads `MadConfig.json` and environment variables prefixed with `MAD_` (eg: `MAD_DiscordToken`).

Copy `MadConfig.example.json` to `MadConfig.json` to start with a safe local configuration.

```json
{
  "DiscordToken": "your-bot-token",
  "DatabasePath": "./mad.db",
  "Debug": true,
  "ManagerGuild": 123456789012345678,
  "MaxRulesPerChannel": 1,
  "MaxRulesPerGuild": 20
}
```

## Running

```sh
dotnet restore
dotnet run --project Mad.Application
```

For a release build:

```sh
dotnet build Mad.slnx --configuration Release
```

The container image stores its SQLite database at `/data/MadDatabase.sqlite`. Mount a persistent volume at `/data`.

## Commands

- `/rule create` creates a deletion rule.
- `/rule list` lists rules.
- `/rule delete` removes a rule by name.

Members need the **Manage Messages** permission to use these commands.
