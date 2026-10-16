using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace ChatManager;

/// <summary>
/// ChatManager configuration, written to the plugin folder as ChatManager.json on first load.
/// </summary>
public sealed class ChatManagerConfig : BasePluginConfig
{
    /// <summary>
    /// Path to CS2-AdminPlus's communication_data.json. AdminPlus has no public API, so ChatManager
    /// reads this file; gag/silence enforcement for AdminPlus auto-enables when the file exists.
    /// Relative paths are resolved against the CS2 game root (the folder that contains addons/).
    /// Leave default unless AdminPlus is installed in a non-standard location.
    /// </summary>
    [JsonPropertyName("AdminPlusDataPath")]
    public string AdminPlusDataPath { get; set; } = "addons/counterstrikesharp/plugins/AdminPlus/communication_data.json";

    // ---- Chat formats: one per message type that exists in the game ----
    // Two channels (say = all chat, say_team = team chat), crossed with the sender's team
    // (CT / T / Spectator) and alive / dead. Unassigned players use the Spectator formats.
    //
    // Placeholders:
    //   {1}                  tags + coloured name
    //   {2}                  coloured message (also {message} / {msg})
    //   {name}               coloured name only
    //   {tag}                provider tags only (VIP, ADMIN, ...)
    //   {location}           sender's current map callout (e.g. BombsiteA); empty if unknown
    // Colour tokens ({red}, {teamcolor}, {default}, ...) are allowed anywhere in a format.
    // An empty format falls back to "{1} : {2}".

    // ---- All chat (say) ----

    /// <summary>All chat, CT, alive.</summary>
    [JsonPropertyName("CS2_Chat_All_CT")]
    public string AllCT { get; set; } = "{1} : {2}";

    /// <summary>All chat, T, alive.</summary>
    [JsonPropertyName("CS2_Chat_All_T")]
    public string AllT { get; set; } = "{1} : {2}";

    /// <summary>All chat, CT, dead.</summary>
    [JsonPropertyName("CS2_Chat_All_CT_Dead")]
    public string AllCTDead { get; set; } = "*DEAD* {1} : {2}";

    /// <summary>All chat, T, dead.</summary>
    [JsonPropertyName("CS2_Chat_All_T_Dead")]
    public string AllTDead { get; set; } = "*DEAD* {1} : {2}";

    /// <summary>All chat, spectator.</summary>
    [JsonPropertyName("CS2_Chat_All_Spec")]
    public string AllSpec { get; set; } = "*SPEC* {1} : {2}";

    // ---- Team chat (say_team) ----

    /// <summary>Team chat, CT, alive, no known location.</summary>
    [JsonPropertyName("CS2_Chat_Team_CT")]
    public string TeamCT { get; set; } = "(Counter-Terrorist) {1} : {2}";

    /// <summary>Team chat, CT, alive, with location. Empty = use CS2_Chat_Team_CT.</summary>
    [JsonPropertyName("CS2_Chat_Team_CT_Loc")]
    public string TeamCTLoc { get; set; } = "(Counter-Terrorist) {1} @ {location} : {2}";

    /// <summary>Team chat, CT, dead.</summary>
    [JsonPropertyName("CS2_Chat_Team_CT_Dead")]
    public string TeamCTDead { get; set; } = "*DEAD*(Counter-Terrorist) {1} : {2}";

    /// <summary>Team chat, T, alive, no known location.</summary>
    [JsonPropertyName("CS2_Chat_Team_T")]
    public string TeamT { get; set; } = "(Terrorist) {1} : {2}";

    /// <summary>Team chat, T, alive, with location. Empty = use CS2_Chat_Team_T.</summary>
    [JsonPropertyName("CS2_Chat_Team_T_Loc")]
    public string TeamTLoc { get; set; } = "(Terrorist) {1} @ {location} : {2}";

    /// <summary>Team chat, T, dead.</summary>
    [JsonPropertyName("CS2_Chat_Team_T_Dead")]
    public string TeamTDead { get; set; } = "*DEAD*(Terrorist) {1} : {2}";

    /// <summary>Team chat, spectator.</summary>
    [JsonPropertyName("CS2_Chat_Team_Spec")]
    public string TeamSpec { get; set; } = "(Spectator) {1} : {2}";

    /// <summary>
    /// Name colour used when no tag provider sets one (CSGO shows names in the team colour).
    /// Use "default" to keep the old behaviour (white name).
    /// </summary>
    [JsonPropertyName("DefaultNameColor")]
    public string DefaultNameColor { get; set; } = "teamcolor";

    /// <summary>Message colour used when no tag provider sets one.</summary>
    [JsonPropertyName("DefaultMessageColor")]
    public string DefaultMessageColor { get; set; } = "default";

    /// <summary>
    /// When true, players that have no tag from any provider also get the CSGO-style formats above.
    /// When false, they keep the stock CS2 chat line.
    /// </summary>
    [JsonPropertyName("FormatPlayersWithoutTags")]
    public bool FormatPlayersWithoutTags { get; set; } = true;

    /// <summary>
    /// Separator inserted BETWEEN two tag prefixes in chat and on the scoreboard. The user wants the
    /// chat tags with no separator, so this defaults to empty (prefixes are concatenated directly).
    /// </summary>
    [JsonPropertyName("TagSeparator")]
    public string TagSeparator { get; set; } = "";

    /// <summary>
    /// Extra chat command triggers to hide, in ADDITION to CounterStrikeSharp's own PublicChatTrigger
    /// and SilentChatTrigger from core.json. A message starting with any of these is treated as a
    /// command and not re-broadcast as a tagged chat line. Defaults to "." (older tag plugins used it)
    /// so ".ws", ".gloves" etc. stay hidden even though "." is not a CounterStrikeSharp trigger.
    /// </summary>
    [JsonPropertyName("ExtraChatTriggers")]
    public List<string> ExtraChatTriggers { get; set; } = ["."];
}
