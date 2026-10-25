// Reading the NWEA Reports Queue table (reports.mapnwea.org/queue).

using System.Globalization;
using System.Text.RegularExpressions;

namespace PlaybookRunner;

public sealed record QueueRow(int Index, long Id, bool Ready, string Text);

public static partial class QueueRows
{
    [GeneratedRegex(@"\b(\d{6,})\b")]
    private static partial Regex ReportId();

    [GeneratedRegex("ready to view", RegexOptions.IgnoreCase)]
    private static partial Regex ReadyText();

    /// <summary>Parse the text of each queue row. Rows without a report id are ignored.</summary>
    public static IReadOnlyList<QueueRow> Parse(IReadOnlyList<string> texts) =>
        texts
            .Select((text, index) => (text, index, m: ReportId().Match(text)))
            .Where(t => t.m.Success)
            .Select(t => new QueueRow(
                t.index,
                long.Parse(t.m.Groups[1].Value, CultureInfo.InvariantCulture),
                ReadyText().IsMatch(t.text),
                t.text))
            .ToList();

    /// <summary>
    /// The newest row (highest report id) that is not in <paramref name="seen"/>, or null.
    /// <paramref name="seen"/> holds ids that were already in the queue or already downloaded, so a
    /// stale "Ready to View" row is never mistaken for the report just requested.
    /// </summary>
    public static QueueRow? Newest(IReadOnlyList<QueueRow> rows, IReadOnlySet<long>? seen = null) =>
        rows.Where(r => seen?.Contains(r.Id) != true).MaxBy(r => r.Id);
}
