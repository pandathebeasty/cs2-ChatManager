using System.Collections.Concurrent;
using ChatManagerApi;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2_SimpleAdminApi;
using Microsoft.Extensions.Logging;

namespace ChatManager;

/// <summary>
/// Central chat/tag broker. It owns the single say / say_team hook and the single writer of
/// player.Clan, and merges tag contributions from any number of providers (VIPCore's VIP_Tags,
/// CS2-Tags, ...). Providers no longer hook chat or write the clan tag themselves, which removes
/// the double-message / overwrite conflicts.
///
///   * Prefix order  : by ChatTag.Order ascending  (VIP=10 before admin=20  ->  "[VIP][ADMIN]")
///   * Name/msg colour: from the tag with the highest ChatTag.ColorPriority (admin wins)
///   * Scoreboard     : the ordered prefixes, stripped of colours, concatenated into player.Clan
///   * Gag            : delegated to CS2-SimpleAdmin / Iks_Admin / CS2-AdminPlus when present
/// </summary>
public sealed class ChatManager : BasePlugin, IPluginConfig<ChatManagerConfig>
{
    public override string ModuleName => "ChatManager";
    public override string ModuleAuthor => "panda";
    public override string ModuleDescription => "CS2 Chat Processor for CounterStrikeSharp";
    public override string ModuleVersion => "1.1.1";
    
    public ChatManagerConfig Config { get; set; } = new();

    private readonly ConcurrentDictionary<string, ChatTagProvider> _providers = new();
    private ICS2_SimpleAdminApi? _simpleAdmin;
    private AdminPlusGagReader? _adminPlusGag;
    private IksAdminGagReader? _iksAdmin;
    private ChatManagerApiImpl? _api;

    // Per-slot cache of the merged tag contributions, refreshed on spawn / toggle / explicit
    // RefreshTags. Both the scoreboard tag and the chat line read from this, so a refresh updates
    // BOTH at once. Null entry = not computed yet -> fall back to a live collect.
    private readonly List<ChatTag>?[] _tagCache = new List<ChatTag>?[70];

    // Chat command triggers (e.g. "!", "/", ".") used to detect command input that must not be
    // re-broadcast as a normal chat line. Built from CounterStrikeSharp's core.json plus extras.
    private string[] _chatTriggers = ["!", "/", "."];

    public void OnConfigParsed(ChatManagerConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        // Publish the shared API so providers can resolve it in their OnAllPluginsLoaded.
        _api = new ChatManagerApiImpl(this);
        Capabilities.RegisterPluginCapability(IChatManagerApi.Capability, () => _api);

        // AdminPlus gag integration (file-based, no public API). Resolve the data path: absolute as
        // given, otherwise relative to the CS2 game root (the folder that contains "addons").
        // AdminPlus auto-detect: enabled when its data file exists.
        {
            var path = Config.AdminPlusDataPath;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(GameRoot(), path);

            if (File.Exists(path))
            {
                _adminPlusGag = new AdminPlusGagReader(path);
                Logger.LogInformation("[ChatManager] AdminPlus gag integration enabled: {Path}", path);
            }
        }

        // The single chat hook for the whole server.
        AddCommandListener("say", OnSay, HookMode.Pre);
        AddCommandListener("say_team", OnSayTeam, HookMode.Pre);

        // Keep both the scoreboard tag and the cached chat tag fresh on spawn.
        RegisterEventHandler<EventPlayerSpawn>((e, _) =>
        {
            var player = e.Userid;
            if (player is { IsValid: true, IsBot: false })
                AddTimer(0.3f, () => RefreshTags(player));
            return HookResult.Continue;
        });

        // Drop the cache when a slot frees up so a reused slot never shows a previous player's tag.
        RegisterListener<Listeners.OnClientDisconnect>(slot =>
        {
            if (slot >= 0 && slot < _tagCache.Length)
                _tagCache[slot] = null;
        });
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        // Soft-resolve SimpleAdmin for gag/silence status. Absence is fine - chat just isn't blocked.
        try
        {
            _simpleAdmin = ICS2_SimpleAdminApi.PluginCapability.Get();
        }
        catch
        {
            _simpleAdmin = null;
        }

        // Iks_Admin via reflection (capability "iksadmin:core"), no DLL dependency.
        _iksAdmin = IksAdminGagReader.TryCreate();

        var gagSources = new List<string>();
        if (_simpleAdmin != null) gagSources.Add("CS2-SimpleAdmin");
        if (_iksAdmin != null) gagSources.Add("Iks_Admin");
        if (_adminPlusGag != null) gagSources.Add("CS2-AdminPlus");

        if (gagSources.Count > 0)
            Logger.LogInformation("[ChatManager] Gag/silence source(s): {Sources}.", string.Join(", ", gagSources));
        else
            Logger.LogInformation("[ChatManager] No admin plugin found (CS2-SimpleAdmin / Iks_Admin / CS2-AdminPlus) - gag/silence checks are disabled.");

        BuildChatTriggers();
    }

    /// <summary>
    /// Builds the set of chat command triggers from CounterStrikeSharp's core.json
    /// (PublicChatTrigger + SilentChatTrigger) plus any extras configured for ChatManager. Reading
    /// the core triggers means ChatManager honours a server that customises them, so a message that
    /// starts with a command trigger is never re-broadcast as a tagged chat line.
    /// </summary>
    private void BuildChatTriggers()
    {
        var triggers = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var t in CoreConfig.PublicChatTrigger)
                if (!string.IsNullOrEmpty(t)) triggers.Add(t);
            foreach (var t in CoreConfig.SilentChatTrigger)
                if (!string.IsNullOrEmpty(t)) triggers.Add(t);
        }
        catch
        {
            // Core config unavailable for some reason - keep the sensible defaults.
            triggers.Add("!");
            triggers.Add("/");
            triggers.Add(".");
        }

        foreach (var t in Config.ExtraChatTriggers)
            if (!string.IsNullOrEmpty(t)) triggers.Add(t);

        _chatTriggers = triggers.ToArray();
        Logger.LogInformation("[ChatManager] Chat command triggers: {Triggers}", string.Join(" ", _chatTriggers));
    }
    
    // --- Provider registry (called by ChatManagerApiImpl) ------------------------------------

    internal void RegisterProvider(string name, ChatTagProvider provider)
    {
        if (string.IsNullOrWhiteSpace(name) || provider == null) return;
        _providers[name] = provider;
        Logger.LogInformation("[ChatManager] Registered tag provider '{Name}'.", name);
    }

    internal void UnregisterProvider(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _providers.TryRemove(name, out _);
    }

    // --- Chat handling ------------------------------------------------------------------------

    private HookResult OnSay(CCSPlayerController? player, CommandInfo info) => HandleChat(player, info, false);
    private HookResult OnSayTeam(CCSPlayerController? player, CommandInfo info) => HandleChat(player, info, true);

    private HookResult HandleChat(CCSPlayerController? player, CommandInfo info, bool teamOnly)
    {
        if (player is not { IsValid: true } || player.IsBot)
            return HookResult.Continue;

        var message = info.GetArg(1);
        if (string.IsNullOrWhiteSpace(message))
            return HookResult.Continue;

        // Never swallow chat commands - let the game / other plugins handle them. The command
        // triggers are read from CounterStrikeSharp's own core.json (PublicChatTrigger /
        // SilentChatTrigger), so if the admin customises them, ChatManager follows automatically.
        if (IsChatTrigger(message))
            return HookResult.Continue;

        // Gag / silence: delegated to the detected admin plugin. A gagged/silenced player sends nothing.
        if (IsChatBlocked(player))
            return HookResult.Handled;

        // Use the cached tags so the chat tag matches the scoreboard exactly. If the cache is cold
        // (e.g. first line before any spawn), compute live and prime the cache.
        var tags = GetTags(player);
        if (tags.Count == 0 && !Config.FormatPlayersWithoutTags)
            return HookResult.Continue; // no provider has a tag -> default chat

        var line = BuildChatLine(player, message, tags, teamOnly);

        if (teamOnly)
        {
            foreach (var receiver in Utilities.GetPlayers())
                if (receiver is { IsValid: true, IsBot: false } && receiver.Team == player.Team)
                    receiver.PrintToChat(line);
        }
        else
        {
            Server.PrintToChatAll(line);
        }

        return HookResult.Handled;
    }

    /// <summary>
    /// True when the message starts with a chat command trigger, so it should not be rendered as a
    /// normal chat line. Uses CounterStrikeSharp's configured PublicChatTrigger / SilentChatTrigger
    /// (core.json) plus any extra triggers from ChatManager's own config.
    /// </summary>
    private bool IsChatTrigger(string message)
    {
        var trimmed = message.TrimStart();
        if (trimmed.Length == 0)
            return false;

        foreach (var trigger in _chatTriggers)
            if (trigger.Length > 0 && trimmed.StartsWith(trigger, StringComparison.Ordinal))
                return true;

        return false;
    }

    private bool IsChatBlocked(CCSPlayerController player)
    {
        // CS2-SimpleAdmin (via its public API).
        if (_simpleAdmin != null)
        {
            try
            {
                var status = _simpleAdmin.GetPlayerMuteStatus(player);
                if (HasActivePenalty(status, PenaltyType.Gag) || HasActivePenalty(status, PenaltyType.Silence))
                    return true;
            }
            catch { /* ignore and fall through */ }
        }

        // Iks_Admin (via its iksadmin:core capability, reflection).
        if (_iksAdmin != null && _iksAdmin.IsGaggedOrSilenced(player))
            return true;

        // CS2-AdminPlus (via its communication_data.json file).
        if (_adminPlusGag != null && _adminPlusGag.IsGagged(player.SteamID))
            return true;

        return false;
    }

    /// <summary>
    /// The CS2 game root (the folder that contains "addons"), derived from ModuleDirectory by
    /// walking up until an "addons" folder is found. Falls back to ModuleDirectory's grandparent.
    /// </summary>
    private string GameRoot()
    {
        var dir = new DirectoryInfo(ModuleDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "addons")))
                return dir.FullName;
            dir = dir.Parent;
        }
        // Fallback: .../csgo/addons/counterstrikesharp/plugins/ChatManager -> up 4 = .../csgo
        return Directory.GetParent(ModuleDirectory)?.Parent?.Parent?.Parent?.FullName ?? ModuleDirectory;
    }

    private static bool HasActivePenalty(
        Dictionary<PenaltyType, List<(DateTime EndDateTime, int Duration, bool Passed)>> status,
        PenaltyType type)
    {
        if (!status.TryGetValue(type, out var list)) return false;
        // A penalty is active if it has not "Passed" and either is permanent (Duration 0) or has not
        // yet reached its end time.
        return list.Any(p => !p.Passed && (p.Duration == 0 || p.EndDateTime > DateTime.UtcNow));
    }

    /// <summary>Collects every provider's tag for the player, ignoring nulls.</summary>
    private List<ChatTag> CollectTags(CCSPlayerController player)
    {
        var result = new List<ChatTag>();
        foreach (var provider in _providers.Values)
        {
            ChatTag? tag;
            try { tag = provider(player); }
            catch { tag = null; } // a misbehaving provider must not break chat

            if (tag != null)
                result.Add(tag);
        }
        return result;
    }

    /// <summary>
    /// Returns the player's tags from the per-slot cache, computing (and caching) them live if the
    /// cache is cold. The cache is what keeps the chat tag and the scoreboard tag in sync.
    /// </summary>
    private List<ChatTag> GetTags(CCSPlayerController player)
    {
        var slot = player.Slot;
        if (slot >= 0 && slot < _tagCache.Length && _tagCache[slot] is { } cached)
            return cached;

        var fresh = CollectTags(player);
        if (slot >= 0 && slot < _tagCache.Length)
            _tagCache[slot] = fresh;
        return fresh;
    }

    // {1} = tags + name, {2}/{message}/{msg} = message, {name} = name only, {tag} = tags only,
    // {location} = sender's map callout.
    private static readonly System.Text.RegularExpressions.Regex PlaceholderRegex =
        new(@"\{(1|2|name|message|msg|tag|location)\}",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The sender's current map callout (e.g. "BombsiteA"), or empty when unknown.</summary>
    private static string GetLocation(CCSPlayerController player)
    {
        try { return player.PlayerPawn.Value?.LastPlaceName?.Trim() ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Picks the configured format for this message: channel (all / team chat), sender team
    /// (CT / T / spectator), alive or dead, and - for living players in team chat - whether a
    /// location is known.
    /// </summary>
    private string PickFormat(CCSPlayerController player, bool teamOnly, string location)
    {
        var c = Config;
        var alive = player.PawnIsAlive;
        var hasLoc = location.Length > 0;

        string? fmt = player.Team switch
        {
            CsTeam.CounterTerrorist => teamOnly
                ? (!alive ? c.TeamCTDead : hasLoc && !string.IsNullOrWhiteSpace(c.TeamCTLoc) ? c.TeamCTLoc : c.TeamCT)
                : (alive ? c.AllCT : c.AllCTDead),
            CsTeam.Terrorist => teamOnly
                ? (!alive ? c.TeamTDead : hasLoc && !string.IsNullOrWhiteSpace(c.TeamTLoc) ? c.TeamTLoc : c.TeamT)
                : (alive ? c.AllT : c.AllTDead),
            _ => teamOnly ? c.TeamSpec : c.AllSpec
        };

        return string.IsNullOrWhiteSpace(fmt) ? "{1} : {2}" : fmt;
    }

    private string BuildChatLine(CCSPlayerController player, string message, List<ChatTag> tags, bool teamOnly)
    {
        var team = player.Team;

        // Prefix: all tag prefixes in Order (VIP first), concatenated with the configured separator
        // (empty by default, so "[VIP][ADMIN]").
        var orderedPrefixes = tags
            .Where(t => !string.IsNullOrEmpty(t.ChatPrefix))
            .OrderBy(t => t.Order)
            .Select(t => ColorResolver.ResolveTokens(t.ChatPrefix, team).Trim(' '))
            .Where(p => p.Length > 0)
            .ToList();

        var prefix = string.Join(Config.TagSeparator, orderedPrefixes);

        // Colours for the NAME and MESSAGE: the highest ColorPriority wins (admin over VIP).
        // On a tie, the higher Order wins (the tag further to the right). Tag prefixes keep their
        // own colours regardless - only name/message colour comes from this source.
        var colorSource = tags
            .OrderByDescending(t => t.ColorPriority)
            .ThenByDescending(t => t.Order)
            .FirstOrDefault();

        var nameColor = ColorResolver.ColorChar(
            string.IsNullOrWhiteSpace(colorSource?.NameColor) ? Config.DefaultNameColor : colorSource!.NameColor, team);
        var msgColor = ColorResolver.ColorChar(
            string.IsNullOrWhiteSpace(colorSource?.MessageColor) ? Config.DefaultMessageColor : colorSource!.MessageColor, team);

        var tagPart = prefix.Length > 0 ? prefix + " " : string.Empty;
        var namePart = $"{nameColor}{player.PlayerName}{ChatColors.Default}";
        var messagePart = $"{msgColor}{message}";
        var location = GetLocation(player);

        // Resolve colour tokens in the FORMAT first, then substitute the placeholders in a single
        // pass so player-supplied text (name / message) is never re-interpreted as a token.
        var resolved = ColorResolver.ResolveTokens(PickFormat(player, teamOnly, location), team);
        var line = PlaceholderRegex.Replace(resolved, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "1" => tagPart + namePart,
            "2" or "message" or "msg" => messagePart,
            "name" => namePart,
            "tag" => tagPart,
            "location" => location,
            _ => m.Value
        });

        // Leading space: CS2 chat only honours a colour on the first character after a space.
        return " " + line;
    }

    // --- Refresh (chat tag cache + scoreboard) ------------------------------------------------

    /// <summary>
    /// Recomputes the player's tags from all providers and applies them to BOTH the chat tag cache
    /// (so the next chat line uses the new tags) AND the scoreboard clan tag. Providers call this
    /// whenever a player's tag could have changed (spawn, VIP toggle, admin change).
    /// </summary>
    internal void RefreshTags(CCSPlayerController player)
    {
        if (player is not { IsValid: true } || player.IsBot || player.IsHLTV)
            return;

        var tags = CollectTags(player);

        // Update the chat-tag cache so chat and scoreboard stay in sync.
        var slot = player.Slot;
        if (slot >= 0 && slot < _tagCache.Length)
            _tagCache[slot] = tags;

        // Apply the scoreboard clan tag: ordered tags (VIP first), colours stripped, concatenated.
        var parts = tags
            .Where(t => !string.IsNullOrEmpty(t.ScoreboardTag))
            .OrderBy(t => t.Order)
            .Select(t => ColorResolver.StripTokens(t.ScoreboardTag))
            .Where(s => s.Length > 0)
            .ToList();

        var clan = string.Join(Config.TagSeparator, parts);

        player.Clan = clan;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_szClan");
    }
}

/// <summary>Thin implementation of the shared API that forwards to the plugin.</summary>
internal sealed class ChatManagerApiImpl(ChatManager plugin) : IChatManagerApi
{
    public void RegisterProvider(string name, ChatTagProvider provider) => plugin.RegisterProvider(name, provider);
    public void UnregisterProvider(string name) => plugin.UnregisterProvider(name);
    public void RefreshTags(CCSPlayerController player) => plugin.RefreshTags(player);
}
