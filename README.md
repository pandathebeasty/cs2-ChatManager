# ChatManager

A central chat/tag broker for CounterStrikeSharp. It owns the single `say` / `say_team` hook and
the single writer of `player.Clan`, and merges tag contributions from any number of provider
plugins into **one** chat line and **one** scoreboard clan tag. Providers no longer hook chat or
write the clan tag themselves, which removes the double-message / tag-overwrite conflicts.

Current version: **1.1.0** (.NET 10).

## How it works

- **Prefix order**: tag prefixes are concatenated in `ChatTag.Order` ascending (VIP = 10 before
  admin = 20 → `[VIP][ADMIN]`).
- **Name / message colour**: taken from the tag with the highest `ChatTag.ColorPriority` (admin wins
  over VIP).
- **Scoreboard**: the ordered prefixes, stripped of colours, written to `player.Clan`.
- **Chat formats**: each chat line is built from a configurable format string, chosen by channel
  (all / team chat), sender team (CT / T / Spectator) and alive / dead.
- **Gag / silence**: a gagged or silenced player's message is swallowed. The admin plugin is detected
  automatically at load.

## Chat formats

Every message type has its own format in the config. Formats use these placeholders:

| Placeholder | Meaning |
| --- | --- |
| `{1}` | tags + coloured name |
| `{2}` / `{message}` / `{msg}` | the coloured message |
| `{name}` | coloured name only |
| `{tag}` | provider tags only |
| `{location}` | sender's current map callout (team chat); empty if unknown |

Colour tokens (`{red}`, `{teamcolor}`, `{default}`, ...) are allowed anywhere in a format. An empty
format falls back to `{1} : {2}`.

## Gag / silence auto-detection

At load, ChatManager detects whichever admin plugin is present (any or all of):

- **CS2-SimpleAdmin** — via its public API (`simpleadmin:api`).
- **Iks_Admin** — via its capability (`iksadmin:core`), through reflection (no DLL dependency).
- **CS2-AdminPlus** — via its `communication_data.json` file (no public API).

Detection is fully automatic — there are no enable/disable config flags. The detected source(s) are
logged. If none is found, gag/silence is simply not enforced and a note is logged.

## Projects

| Project | What it is |
| --- | --- |
| `ChatManager` | The plugin: the chat hook, tag merge, scoreboard writer, gag detection. |
| `ChatManagerApi` | The shared API (`IChatManagerApi`, `ChatTag`, `ChatTagProvider`). Ships once in `addons/counterstrikesharp/shared/` and is referenced by every provider plugin. |

## For provider plugins

Reference `ChatManagerApi.dll`, resolve the API in `OnAllPluginsLoaded`, and register a provider:

```csharp
private readonly PluginCapability<IChatManagerApi?> _cap = IChatManagerApi.Capability;
private IChatManagerApi? _chat;

public override void OnAllPluginsLoaded(bool hotReload)
{
    _chat = _cap.Get();
    _chat?.RegisterProvider("myplugin", GetTag);
}

private ChatTag? GetTag(CCSPlayerController player)
{
    // return null when this player has no tag from your plugin
    return new ChatTag
    {
        ChatPrefix   = "{gold}[VIP]",
        ScoreboardTag = "VIP",
        NameColor    = "gold",
        MessageColor = "default",
        Order        = 10,   // lower = further left
        ColorPriority = 50   // higher = wins the name/message colour
    };
}
```

Call `RefreshTags(player)` whenever a player's tag could have changed (spawn, VIP toggle, admin
change) so chat and scoreboard update immediately.

## Configuration

`addons/counterstrikesharp/configs/plugins/ChatManager/ChatManager.json`:

| Key | Default | Description |
| --- | --- | --- |
| `AdminPlusDataPath` | `addons/.../AdminPlus/communication_data.json` | Path to AdminPlus's data file (relative to the game root). AdminPlus gag enforcement auto-enables when this file exists. |
| `CS2_Chat_All_CT` / `_T` | `{1} : {2}` | All-chat format for alive CT / T. |
| `CS2_Chat_All_CT_Dead` / `_T_Dead` | `*DEAD* {1} : {2}` | All-chat format for dead CT / T. |
| `CS2_Chat_All_Spec` | `*SPEC* {1} : {2}` | All-chat format for spectators. |
| `CS2_Chat_Team_CT` / `_T` | `(Counter-Terrorist) {1} : {2}` | Team-chat format, alive, no location. |
| `CS2_Chat_Team_CT_Loc` / `_T_Loc` | `... {1} @ {location} : {2}` | Team-chat format, alive, with location. Empty = use the non-loc format. |
| `CS2_Chat_Team_CT_Dead` / `_T_Dead` | `*DEAD*(...) {1} : {2}` | Team-chat format, dead. |
| `CS2_Chat_Team_Spec` | `(Spectator) {1} : {2}` | Team-chat format for spectators. |
| `DefaultNameColor` | `teamcolor` | Name colour when no provider sets one. |
| `DefaultMessageColor` | `default` | Message colour when no provider sets one. |
| `FormatPlayersWithoutTags` | `true` | Apply the formats to everyone, not only tagged players. `false` keeps the stock CS2 line for untagged players. |
| `TagSeparator` | `` (empty) | Inserted between two tag prefixes. Empty concatenates them (`[VIP][ADMIN]`). |
| `ExtraChatTriggers` | `["."]` | Extra chat command triggers to hide, in addition to CounterStrikeSharp's own `PublicChatTrigger` / `SilentChatTrigger`. |

## Build

Requires the .NET 10 SDK.

```
dotnet build ChatManager/ChatManager.csproj -c Release
```

Ship `ChatManager.dll` (plugins folder) and `ChatManagerApi.dll` (shared folder). Do not ship
`CounterStrikeSharp.API.dll`.
