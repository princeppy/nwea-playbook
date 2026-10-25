// ${name} substitution and file-name cleaning.

using System.Text.RegularExpressions;

namespace PlaybookRunner;

public static partial class Vars
{
    [GeneratedRegex(@"[\\/:*?""<>|]+")]
    private static partial Regex UnsafePathChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\$\{(\w+)\}")]
    private static partial Regex Placeholder();

    /// <summary>Make a value safe as part of a file name: "Abbasi, Nazneen" -> "Abbasi Nazneen".</summary>
    public static string SanitizeForPath(string value) =>
        Whitespace().Replace(UnsafePathChars().Replace(value, "-").Replace(",", ""), " ").Trim();

    /// <summary>
    /// Replace every ${name} in <paramref name="text"/> with its value. Names listed in
    /// <paramref name="pathVars"/> are cleaned for use in file names. An unknown name throws.
    /// </summary>
    public static string Substitute(
        string text,
        IReadOnlyDictionary<string, string> vars,
        IReadOnlySet<string>? pathVars = null) =>
        Placeholder().Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (!vars.TryGetValue(name, out var value)) throw new PlaybookException($"Unknown variable ${{{name}}}");
            return pathVars?.Contains(name) == true ? SanitizeForPath(value) : value;
        });
}
