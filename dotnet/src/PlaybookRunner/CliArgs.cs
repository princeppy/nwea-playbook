// Command-line arguments: PlaybookRunner <playbook.yaml> [--headed] [--keep] [--slow=300] [name=value ...]

using System.Globalization;
using System.Text.RegularExpressions;

namespace PlaybookRunner;

public sealed partial record CliArgs(
    string? File,
    bool Headed,
    bool Keep,
    int SlowMo,
    IReadOnlyDictionary<string, string> Vars)
{
    private const int HeadedSlowMo = 200;

    public const string Usage =
        "Usage: dotnet PlaybookRunner.dll <playbook.yaml> [--headed] [--keep] [--slow=300] [name=value ...]\n" +
        "  e.g. ./dotnet/run.sh \"Fall 2026-2027\" --headed\n" +
        "       dotnet PlaybookRunner.dll --install-browsers   (download Chromium once)";

    [GeneratedRegex(@"\.ya?ml$", RegexOptions.IgnoreCase)]
    private static partial Regex YamlFile();

    [GeneratedRegex(@"^(\w+)=(.*)$", RegexOptions.Singleline)]
    private static partial Regex NameValue();

    public static CliArgs Parse(IReadOnlyList<string> argv)
    {
        // name=value pairs are never the playbook, even if the value ends in .yml (e.g. a password)
        var file = argv.FirstOrDefault(a => !a.Contains('=') && YamlFile().IsMatch(a));
        var keep = argv.Contains("--keep");
        var headed = keep || argv.Contains("--headed");
        var slowArg = argv.FirstOrDefault(a => a.StartsWith("--slow=", StringComparison.Ordinal));
        var slowMo = slowArg is not null
            ? int.TryParse(slowArg["--slow=".Length..], CultureInfo.InvariantCulture, out var ms) ? ms : 0
            : headed ? HeadedSlowMo : 0;
        var vars = argv
            .Select(a => NameValue().Match(a))
            .Where(m => m.Success)
            .Aggregate(
                new Dictionary<string, string>(),
                (d, m) => new Dictionary<string, string>(d) { [m.Groups[1].Value] = m.Groups[2].Value });
        return new CliArgs(file, headed, keep, slowMo, vars);
    }
}
