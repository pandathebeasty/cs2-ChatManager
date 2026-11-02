using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;

namespace ChatManagerApi;

/// <summary>
/// A single tag contribution for one player, returned by a provider (VIPCore's VIP_Tags,
/// CS2-Tags, etc.). The ChatManager plugin collects contributions from every registered provider
/// and merges them into ONE chat line and ONE scoreboard clan tag, so the plugins no longer fight
/// over the chat hook or over player.Clan.
///
/// Positioning vs. colour are intentionally separate:
///   * Order        - where the prefix sits left-to-right (lower = further left). VIP uses a lower
///                    Order than admin so the scoreboard/chat reads "[VIP][ADMIN]".
///   * ColorPriority - who wins the NAME and MESSAGE colours when several tags are active (higher
///                    wins). Admin uses a higher ColorPriority than VIP, so even though the VIP
///                    prefix is on the left, the admin colours are applied to the name/message.
/// </summary>
public sealed class ChatTag
{
    /// <summary>
    /// Plain-text tag for the scoreboard (player.Clan). No colour tokens - the scoreboard cannot
    /// render chat colours. Empty = this provider adds nothing to the scoreboard.
    /// </summary>
    public string ScoreboardTag { get; set; } = string.Empty;

    /// <summary>
    /// Prefix shown before the player name in chat. May contain colour tokens like {red} or
    /// {teamcolor}; the ChatManager resolves them. Empty = no chat prefix from this provider.
    /// </summary>
    public string ChatPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Colour applied to the player NAME in chat. Colour name ("lime", "gold", "teamcolor", ...)
    /// or empty to leave it to a higher-priority provider / the default.
    /// </summary>
    public string NameColor { get; set; } = string.Empty;

    /// <summary>
    /// Colour applied to the MESSAGE body in chat. Colour name or empty.
    /// </summary>
    public string MessageColor { get; set; } = string.Empty;

    /// <summary>
    /// Left-to-right position of the prefix (lower = further left). Convention: VIP = 10, admin = 20.
    /// </summary>
    public int Order { get; set; } = 100;

    /// <summary>
    /// Who wins the name/message colour when multiple tags are active (higher wins).
    /// Convention: admin = 100, VIP = 50.
    /// </summary>
    public int ColorPriority { get; set; }
}

/// <summary>
/// A provider callback. The ChatManager calls it for a given player and expects either a ChatTag
/// describing that player's current tag, or null when the provider has nothing for this player
/// (e.g. a non-VIP for the VIP provider, or a player with no matching rule in CS2-Tags).
/// </summary>
public delegate ChatTag? ChatTagProvider(CCSPlayerController player);

/// <summary>
/// A moderation filter. The ChatManager calls every registered filter for each incoming chat line
/// BEFORE it builds/sends the tagged line. Return true to BLOCK the message (ChatManager will not
/// broadcast it); return false to allow it. This lets a moderation plugin (e.g. AutoMod) inspect and
/// block chat even for players who have a tag, instead of fighting ChatManager over the say hook.
/// </summary>
/// <param name="player">The sender.</param>
/// <param name="message">The raw chat text (as typed).</param>
/// <param name="teamOnly">True for say_team, false for global say.</param>
/// <returns>True to block (drop) the message; false to allow it through.</returns>
public delegate bool ChatMessageFilter(CCSPlayerController player, string message, bool teamOnly);

/// <summary>
/// A read-only chat observer. ChatManager calls every registered observer for EACH incoming chat
/// line - tagged or not, say or say_team, and even for messages that are later blocked (gag /
/// filter) - so a watcher sees every message ATTEMPT. The observer must not mutate anything and its
/// return is ignored; it cannot block a message (use a <see cref="ChatMessageFilter"/> for that).
/// This is what lets a spy/logger plugin (e.g. AdminUtils) observe chat even for players who have a
/// tag, instead of racing ChatManager over the say hook (which ChatManager consumes with Handled).
/// </summary>
/// <param name="player">The sender.</param>
/// <param name="message">The raw chat text (as typed).</param>
/// <param name="teamOnly">True for say_team, false for global say.</param>
public delegate void ChatObserver(CCSPlayerController player, string message, bool teamOnly);

/// <summary>
/// The shared ChatManager API. The ChatManager plugin publishes an implementation through the
/// capability below; provider plugins resolve it in OnAllPluginsLoaded and register themselves.
/// </summary>
public interface IChatManagerApi
{
    /// <summary>Capability name used to share the API across plugins.</summary>
    public static readonly PluginCapability<IChatManagerApi?> Capability = new("chatmanager:api");

    /// <summary>
    /// Registers a tag provider under a unique name. Calling again with the same name replaces the
    /// previous provider (safe for hot reloads).
    /// </summary>
    /// <param name="name">Unique provider id, e.g. "vipcore" or "cs2tags".</param>
    /// <param name="provider">Callback returning the player's tag, or null for no tag.</param>
    void RegisterProvider(string name, ChatTagProvider provider);

    /// <summary>Removes a previously registered provider. No-op if it was not registered.</summary>
    void UnregisterProvider(string name);

    /// <summary>
    /// Registers a moderation filter under a unique name. ChatManager calls every filter for each
    /// chat line before sending it; if any filter returns true, the message is blocked and not
    /// broadcast. Calling again with the same name replaces the previous filter (safe for reloads).
    /// Use this from a moderation plugin so chat is checked even for players who have a tag.
    /// </summary>
    /// <param name="name">Unique filter id, e.g. "automod".</param>
    /// <param name="filter">Callback returning true to block the message.</param>
    void RegisterMessageFilter(string name, ChatMessageFilter filter);

    /// <summary>Removes a previously registered message filter. No-op if it was not registered.</summary>
    void UnregisterMessageFilter(string name);

    /// <summary>
    /// Registers a read-only chat observer under a unique name. ChatManager calls every observer for
    /// each incoming chat line (tagged or not, say or say_team, including messages later blocked by a
    /// gag or filter), so a watcher sees every message attempt. The observer cannot block a message.
    /// Calling again with the same name replaces the previous observer (safe for hot reloads).
    /// Use this from a spy/logging plugin so chat is observed even for players who have a tag.
    /// </summary>
    /// <param name="name">Unique observer id, e.g. "adminutils-spy".</param>
    /// <param name="observer">Callback invoked for each chat line.</param>
    void RegisterChatObserver(string name, ChatObserver observer);

    /// <summary>Removes a previously registered chat observer. No-op if it was not registered.</summary>
    void UnregisterChatObserver(string name);

    /// <summary>
    /// Recomputes a player's tags from all providers and applies them to BOTH the chat tag (used by
    /// the next chat line) AND the scoreboard clan tag. Providers call this when a player's tag could
    /// have changed (spawn, VIP toggle, admin change) so chat and scoreboard update immediately.
    /// </summary>
    void RefreshTags(CCSPlayerController player);
}
