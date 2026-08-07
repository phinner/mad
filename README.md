# M.A.D

Message Auto Delete, a small Discord bot that deletes messages satisfying defined rules.

## Installation

[Add M.A.D to your Discord server](https://discord.com/oauth2/authorize?client_id=1534942157306073259).

## Requirements

- .NET 10 SDK
- A Discord bot token
- `View Channel`, `Read Message History`, and `Manage Messages` permissions in managed channels

## Configuration

Mad reads `MadConfig.json` and environment variables prefixed with `MAD_`. Copy
`MadConfig.example.json` to `MadConfig.json` to start with a safe local configuration;
`MadConfig.json` is ignored by Git because it contains the bot token.

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

The equivalent environment variables are:

```sh
export MAD_DiscordToken="your-bot-token"
export MAD_DatabasePath="./mad.db"
export MAD_Debug="true"
export MAD_ManagerGuild="123456789012345678"
export MAD_MaxRulesPerChannel="1"
export MAD_MaxRulesPerGuild="20"
```

`MaxRulesPerChannel` defaults to `1`, and `MaxRulesPerGuild` defaults to `20`.

## Running

```sh
dotnet restore
dotnet run --project Mad.Application
```

For a release build:

```sh
dotnet build Mad.slnx --configuration Release
```

## Commands

- `/rule create` creates a deletion rule.
- `/rule list` lists rules.
- `/rule delete` removes a rule by name.

Members need the **Manage Messages** permission to use `/rule create` and `/rule delete`.
