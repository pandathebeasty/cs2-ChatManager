using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace ChatManager;

/// <summary>
/// Converts colour NAMES and {color}/{teamcolor} tokens into CS2 chat control characters. Shared by
/// the chat line builder so every provider's colours render the same way.
/// </summary>
public static class ColorResolver
{
    private static readonly Regex TokenRegex = new(@"\{[a-zA-Z]+\}", RegexOptions.Compiled);

    /// <summary>
    /// Expands {color} and {teamcolor} tokens inside a string into chat control chars for a given
    /// team. Unknown tokens are left untouched.
    /// </summary>
    public static string ResolveTokens(string? text, CsTeam team)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        return TokenRegex.Replace(text, m =>
        {
            var name = m.Value[1..^1]; // strip the braces
            var c = NameToChar(name, team);
            // Default resolves to '\x01'; if the token was not a known colour, keep it verbatim.
            return c.HasValue ? c.Value.ToString() : m.Value;
        });
    }

    /// <summary>
    /// Removes every {color} token (for plain-text scoreboard clan tags, which cannot show colours).
    /// </summary>
    public static string StripTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return TokenRegex.Replace(text, string.Empty).Trim();
    }

    /// <summary>
    /// Resolves a colour NAME ("lime", "gold", "teamcolor", ...) to a chat control char. Returns the
    /// Default colour char when the name is empty or unknown, so callers always get a usable colour.
    /// </summary>
    public static char ColorChar(string? name, CsTeam team)
        => NameToChar(name, team) ?? ChatColors.Default;

    private static char? NameToChar(string? name, CsTeam team)
    {
        var n = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (n.Length == 0) return null;
        // Normalise separators so "light_blue", "light blue" and "lightblue" all match.
        n = n.Replace("_", string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);

        if (n is "teamcolor" or "team")
            return ChatColors.ForTeam(team);

        // Every colour exposed by CounterStrikeSharp's ChatColors, with common spelling aliases.
        return n switch
        {
            "default" => ChatColors.Default,
            "white" => ChatColors.White,
            "darkred" => ChatColors.DarkRed,
            "green" => ChatColors.Green,
            "lightyellow" => ChatColors.LightYellow,
            "lightblue" => ChatColors.LightBlue,
            "olive" => ChatColors.Olive,
            "lime" => ChatColors.Lime,
            "red" => ChatColors.Red,
            "lightpurple" => ChatColors.LightPurple,
            "purple" => ChatColors.Purple,
            "grey" or "gray" => ChatColors.Grey,
            "yellow" => ChatColors.Yellow,
            "gold" => ChatColors.Gold,
            "silver" => ChatColors.Silver,
            "blue" => ChatColors.Blue,
            "darkblue" => ChatColors.DarkBlue,
            "bluegrey" or "bluegray" => ChatColors.BlueGrey,
            "magenta" => ChatColors.Magenta,
            "lightred" => ChatColors.LightRed,
            "orange" => ChatColors.Orange,
            _ => null
        };
    }
}
