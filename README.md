# ChatManager

A central chat/tag broker for CounterStrikeSharp. It owns the single `say` / `say_team` hook and
the single writer of `player.Clan`, and merges tag contributions from any number of provider
plugins (VIPCore's VIP_Tags, CS2-Tags, GoldMember, ...) into **one** chat line and **one** scoreboard
clan tag. Providers no longer hook chat or write the clan tag themselves, which removes the
double-message / tag-overwrite conflicts.

## How it works

- **Prefix order**: tag prefixes are concatenated in `ChatTag.Order` ascending (VIP = 10 before
  admin = 20 → `[VIP][ADMIN]`).
- **Name / message colour**: taken from the tag with the highest `ChatTag.ColorPriority` (admin wins
  over VIP).
- **Scoreboard**: the ordered prefixes, stripped of colours, written to `player.Clan`.
- **Gag / silence**: a gagged or silenced player's message is swallowed. The admin plugin is detected
  automatically at load.

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
change) so chat and scoreboard update immediately. Known provider ids: `vipcore`, `cs2tags`,
`goldmember`.

## Configuration

`addons/counterstrikesharp/configs/plugins/ChatManager/ChatManager.json`:

| Key | Default | Description |
| --- | --- | --- |
| `AdminPlusDataPath` | `addons/counterstrikesharp/plugins/AdminPlus/communication_data.json` | Path to AdminPlus's data file (relative to the game root). AdminPlus gag enforcement auto-enables when this file exists. |
| `DeadPrefix` | `{default}* ` | Prefix shown before dead players' tags. Empty to disable. |
| `TeamPrefix` | `{teamcolor}(TEAM) ` | Shown before team-only messages. Empty to disable. |
| `TagSeparator` | `` (empty) | Inserted between two tag prefixes. Empty concatenates them (`[VIP][ADMIN]`). |
| `ExtraChatTriggers` | `["."]` | Extra chat command triggers to hide, in addition to CounterStrikeSharp's own `PublicChatTrigger` / `SilentChatTrigger`. |

## Build

Requires the .NET 10 SDK.

```
dotnet build ChatManager/ChatManager.csproj -c Release
```

Ship `ChatManager.dll` (plugins folder) and `ChatManagerApi.dll` (shared folder). Do not ship
`CounterStrikeSharp.API.dll`.
