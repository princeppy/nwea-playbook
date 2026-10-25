namespace PlaybookRunner.Tests;

public class QueueAndStatsTests
{
    [Fact]
    public void QueueRows_read_report_id_and_status_ignoring_rows_without_an_id()
    {
        var rows = QueueRows.Parse(
        [
            "DATE CREATED REPORT TYPE DETAILS REPORT ID STATUS ACTION",
            "10/25/26 Achievement Status and Growth CSV 865.5 KB 183651328 Ready to View Deleting on 11/08/26 Download",
            "10/25/26 Achievement Status and Growth CSV 183651330 Processing",
        ]);
        Assert.Equal([(1, 183651328L, true), (2, 183651330L, false)], rows.Select(r => (r.Index, r.Id, r.Ready)));
    }

    [Fact]
    public void Newest_picks_the_highest_report_id()
    {
        var rows = QueueRows.Parse(["a 183651331 Ready to View", "b 183651334 Processing", "c 183651330 Ready to View"]);
        Assert.Equal(183651334, QueueRows.Newest(rows)?.Id);
        Assert.Null(QueueRows.Newest([]));
    }

    [Fact]
    public void Newest_ignores_reports_that_were_already_in_the_queue()
    {
        var rows = QueueRows.Parse(["a 183651331 Ready to View", "b 183651334 Ready to View"]);
        Assert.Equal(183651331, QueueRows.Newest(rows, new HashSet<long> { 183651334 })?.Id);
        Assert.Null(QueueRows.Newest(rows, new HashSet<long> { 183651331, 183651334 }));
    }

    [Fact]
    public void Stats_return_new_values_and_never_mutate()
    {
        var a = Stats.Empty;
        var b = a.AddDownload().AddSkip("no scored students");
        Assert.Equal(0, a.Downloaded);
        Assert.Empty(a.Skipped);
        Assert.Equal(1, b.Downloaded);
        Assert.Equal(1, b.SkipCount("no scored students"));
        Assert.Single(b.AddFailure(new Failure("x", "y")).Failures);
        Assert.Empty(b.Failures);
    }

    [Fact]
    public void StatsRecorder_and_summary_lines()
    {
        var r = new StatsRecorder();
        r.Download();
        r.Skip("already downloaded");
        r.Skip("already downloaded");
        r.Fail("course \"Algebra 1\"", "Timed out");
        Assert.Equal(
        [
            "Summary",
            "  downloaded: 1",
            "  skipped (already downloaded): 2",
            "  failed: 1",
            "    - course \"Algebra 1\": Timed out",
        ],
            r.Value.FormatSummary());
    }
}
