// Counts for the end-of-run summary: downloaded, skipped (by reason) and failed.

using System.Collections.Immutable;

namespace PlaybookRunner;

public sealed record Failure(string Item, string Error);

/// <summary>Immutable run counts. Skips are kept per reason, in the order reasons first occurred.</summary>
public sealed record Stats(
    int Downloaded,
    ImmutableList<KeyValuePair<string, int>> Skipped,
    ImmutableList<Failure> Failures)
{
    public static Stats Empty { get; } = new(0, [], []);

    public Stats AddDownload() => this with { Downloaded = Downloaded + 1 };

    public Stats AddSkip(string reason)
    {
        var i = Skipped.FindIndex(kv => kv.Key == reason);
        return this with
        {
            Skipped = i < 0
                ? Skipped.Add(KeyValuePair.Create(reason, 1))
                : Skipped.SetItem(i, KeyValuePair.Create(reason, Skipped[i].Value + 1)),
        };
    }

    public Stats AddFailure(Failure failure) => this with { Failures = Failures.Add(failure) };

    public int SkipCount(string reason) => Skipped.FirstOrDefault(kv => kv.Key == reason).Value;

    public IReadOnlyList<string> FormatSummary() =>
    [
        "Summary",
        $"  downloaded: {Downloaded}",
        .. Skipped.Select(kv => $"  skipped ({kv.Key}): {kv.Value}"),
        $"  failed: {Failures.Count}",
        .. Failures.Select(f => $"    - {f.Item}: {f.Error}"),
    ];
}

/// <summary>Holds the current <see cref="Stats"/>; each record call swaps in a new immutable value.</summary>
public sealed class StatsRecorder
{
    public Stats Value { get; private set; } = Stats.Empty;

    public void Download() => Value = Value.AddDownload();

    public void Skip(string reason) => Value = Value.AddSkip(reason);

    public void Fail(string item, string error) => Value = Value.AddFailure(new Failure(item, error));
}
