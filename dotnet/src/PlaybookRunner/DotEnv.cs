// Reads a .env file (NAME="value" lines) the way Node's process.loadEnvFile does:
// comments and blank lines are skipped, quotes are removed, and variables already
// set in the real environment win over the file.

using System.Collections;
using System.Text.RegularExpressions;

namespace PlaybookRunner;

public static partial class DotEnv
{
    [GeneratedRegex(@"^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$")]
    private static partial Regex Line();

    /// <summary>Parse .env text into name/value pairs (later lines win).</summary>
    public static IReadOnlyDictionary<string, string> Parse(string text) =>
        text.Split('\n')
            .Select(l => Line().Match(l.TrimEnd('\r')))
            .Where(m => m.Success)
            .Aggregate(
                new Dictionary<string, string>(),
                (d, m) => new Dictionary<string, string>(d) { [m.Groups[1].Value] = Value(m.Groups[2].Value) });

    private static string Value(string raw)
    {
        var v = raw.Trim();
        if (v.Length > 0 && v[0] is '"' or '\'' or '`')
        {
            var end = v.IndexOf(v[0], 1);
            if (end > 0)
            {
                var inner = v[1..end];
                return v[0] == '"' ? inner.Replace("\\n", "\n", StringComparison.Ordinal) : inner;
            }
        }
        var hash = v.IndexOf('#');
        return (hash >= 0 ? v[..hash] : v).Trim();
    }

    /// <summary>The process environment merged over the .env file at <paramref name="path"/> (if it exists).</summary>
    public static IReadOnlyDictionary<string, string> Load(string path)
    {
        var fromFile = File.Exists(path) ? Parse(File.ReadAllText(path)) : new Dictionary<string, string>();
        var real = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Select(e => KeyValuePair.Create((string)e.Key, (string?)e.Value ?? ""));
        return fromFile.Concat(real)
            .GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.Last().Value);
    }
}
