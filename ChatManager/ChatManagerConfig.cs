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

    /// <summary>
    /// Dead players get this prefix before their tag (chat colour tokens allowed). Empty to disable.
    /// </summary>
    [JsonPropertyName("DeadPrefix")]
    public string DeadPrefix { get; set; } = "{default}* ";

    /// <summary>
    /// Shown before team-only messages, e.g. "(T)" / "(CT)". {teamcolor} is resolved per sender.
    /// Empty to disable the team marker.
    /// </summary>
    [JsonPropertyName("TeamPrefix")]
    public string TeamPrefix { get; set; } = "{teamcolor}(TEAM) ";

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
