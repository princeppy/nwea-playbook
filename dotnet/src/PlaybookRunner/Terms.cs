// NWEA terms ("Fall 2026-2027") and the seasons a school tests in.

using System.Globalization;
using System.Text.RegularExpressions;

namespace PlaybookRunner;

public sealed record Term(string Label, string Season, int StartYear, int EndYear);

public static partial class Terms
{
    public static readonly IReadOnlyList<string> Seasons = ["Fall", "Winter", "Spring", "Summer"];

    [GeneratedRegex(@"^(Fall|Winter|Spring|Summer) (\d{4})-(\d{4})$")]
    private static partial Regex TermPattern();

    [GeneratedRegex(@"\b(Fall|Winter|Spring|Summer)\b")]
    private static partial Regex SeasonWord();

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separators();

    /// <summary>Parse "Fall, Spring" (or "Fall Spring") into a season list. Unknown words throw.</summary>
    public static IReadOnlyList<string> ParseSeasons(string text)
    {
        var words = Separators().Split(text).Where(w => w.Length > 0).ToList();
        if (words.Count == 0)
        {
            throw new PlaybookException($"testedSeasons is empty. List at least one of: {string.Join(", ", Seasons)}");
        }
        var bad = words.Where(w => !Seasons.Contains(w)).ToList();
        if (bad.Count > 0)
        {
            throw new PlaybookException(
                $"Unknown season(s) in testedSeasons: {string.Join(", ", bad)}. Use: {string.Join(", ", Seasons)}");
        }
        return words.Distinct().ToList();
    }

    /// <summary>Validate a term label against the school's tested seasons. Throws a readable error.</summary>
    public static Term ParseTerm(string label, IReadOnlyList<string> tested)
    {
        var m = TermPattern().Match(label.Trim());
        if (!m.Success)
        {
            throw new PlaybookException(
                $"Term \"{label}\" is not valid. Write it as NWEA shows it, e.g. \"Fall 2026-2027\" or \"Spring 2025-2026\".");
        }
        var season = m.Groups[1].Value;
        var startYear = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var endYear = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (endYear != startYear + 1)
        {
            throw new PlaybookException(
                $"Term \"{label}\": the academic years must be consecutive, e.g. {startYear}-{startYear + 1}.");
        }
        if (!tested.Contains(season))
        {
            throw new PlaybookException(
                $"Term \"{label}\": this school only tests in {string.Join(" and ", tested)} (testedSeasons).");
        }
        return new Term(m.Value, season, startYear, endYear);
    }

    /// <summary>Every season named in a text, e.g. "Fall 2026 - Winter 2027 ..." -> [Fall, Winter].</summary>
    public static IReadOnlyList<string> SeasonsIn(string text) =>
        SeasonWord().Matches(text).Select(m => m.Groups[1].Value).ToList();

    /// <summary>A growth comparison period is kept only if every season it names is tested.</summary>
    public static bool PeriodAllowed(string label, IReadOnlyList<string> tested) =>
        SeasonsIn(label).All(tested.Contains);
}
