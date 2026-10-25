// Small text helpers shared by the runner.

using System.Text.RegularExpressions;

namespace PlaybookRunner;

public static partial class TextUtil
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Collapse runs of whitespace (including &amp;nbsp;) to one space and trim.</summary>
    public static string Normalize(string s) => Whitespace().Replace(s, " ").Trim();

    /// <summary>
    /// Index of the option that matches <paramref name="value"/>: an exact match first, otherwise
    /// an option that is the value followed by a bracketed note, such as
    /// "Fall 2026-2027 (Most Recent)" for "Fall 2026-2027". Returns -1 if none match.
    /// </summary>
    public static int MatchOptionIndex(IReadOnlyList<string> options, string value)
    {
        var wanted = Normalize(value);
        var names = options.Select(Normalize).ToList();
        var exact = names.IndexOf(wanted);
        return exact >= 0 ? exact : names.FindIndex(n => n.StartsWith($"{wanted} (", StringComparison.Ordinal));
    }
}
