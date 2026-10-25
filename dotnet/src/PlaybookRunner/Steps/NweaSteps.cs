// NWEA-specific steps:
//   checkTerm           validate the input term before anything opens
//   nweaQueueClear      empty the Reports Queue before any report is requested
//   nweaQueueDownload   wait for the new report, download it, then remove it from the queue

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PlaybookRunner.Steps;

public static class NweaSteps
{
    private const int QueueTimeoutMs = 15 * 60_000;
    private const int QueuePollMs = 15_000;
    private const int DownloadTimeoutMs = 120_000;

    public static IReadOnlyDictionary<string, StepHandler> All { get; } = new Dictionary<string, StepHandler>
    {
        ["checkTerm"] = CheckTerm,
        ["nweaQueueClear"] = QueueClearAsync,
        ["nweaQueueDownload"] = QueueDownloadAsync,
    };

    /// <summary>Queue data rows, optionally only those of one report type.</summary>
    private static ILocator QueueRowsOf(Ctx ctx, string? report = null) =>
        report is null
            ? ctx.Page.GetByRole(AriaRole.Row)
            : ctx.Page.GetByRole(AriaRole.Row).Filter(new() { HasText = report });

    /// <summary>
    /// The row for one report id, found fresh each time (the table may re-render).
    /// HasText sees cell texts run together ("...Growth183651331Ready..."), so \b would not
    /// match; digit lookarounds do.
    /// </summary>
    private static ILocator RowById(Ctx ctx, long id) =>
        ctx.Page.GetByRole(AriaRole.Row).Filter(new() { HasTextRegex = new Regex($@"(?<!\d){id}(?!\d)") }).First;

    private static async Task<IReadOnlyList<QueueRow>> ReadRowsAsync(Ctx ctx, string? report = null) =>
        QueueRows.Parse(await QueueRowsOf(ctx, report).AllInnerTextsAsync());

    private static async Task<IReadOnlyList<long>> ListIdsAsync(Ctx ctx) =>
        (await ReadRowsAsync(ctx)).Select(r => r.Id).ToList();

    /// <summary>Reload the queue until a new <paramref name="report"/> row (not yet seen) says Ready to View.</summary>
    private static async Task<QueueRow> WaitForReadyRowAsync(Ctx ctx, string report, int timeoutMs, int pollMs)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            await ctx.SettleAsync();
            await ctx.DismissPopupsAsync();
            var row = QueueRows.Newest(await ReadRowsAsync(ctx, report), ctx.Rt.SeenReports);
            if (row is { Ready: true }) return row;
            if (clock.ElapsedMilliseconds > timeoutMs)
            {
                throw new PlaybookException(
                    $"\"{report}\" was not ready in the Reports Queue after {Math.Round(timeoutMs / 60_000.0)} min.");
            }
            ctx.Log(row is not null
                ? $"report {row.Id} not ready yet, checking again in {CtxExtensions.Seconds(pollMs)}s"
                : "waiting for the new report to appear");
            await ctx.Page.WaitForTimeoutAsync(pollMs);
            await ctx.Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        }
    }

    /// <summary>Tick every row (the header's select-all box first, row by row if that misses some).</summary>
    private static async Task TickAllAsync(Ctx ctx, IReadOnlyList<long> ids)
    {
        try
        {
            await QueueUi.TickAsync(ctx, ctx.Page.GetByRole(AriaRole.Row).First);
        }
        catch (PlaybookException)
        {
            // no select-all box: tick the rows one by one
        }
        foreach (var id in ids)
        {
            if (await QueueUi.SelectedCountAsync(ctx.Page) == ids.Count) return;
            await QueueUi.TickAsync(ctx, RowById(ctx, id));
        }
    }

    /// <summary>checkTerm: { term: "${term}", seasons: "${testedSeasons}" } -- stop early on a bad term.</summary>
    private static Task CheckTerm(Ctx ctx, StepArg a)
    {
        var seasons = Terms.ParseSeasons(ctx.Sub(a["seasons"]));
        var term = Terms.ParseTerm(ctx.Sub(a["term"]), seasons);
        ctx.Log($"term OK: {term.Label} (tested seasons: {string.Join(", ", seasons)})");
        return Task.CompletedTask;
    }

    /// <summary>
    /// nweaQueueClear: {} -- run on the Reports Queue page. Removes every report in the queue,
    /// so no old report can be mistaken for a new one. Any that will not go are logged and ignored.
    /// </summary>
    private static async Task QueueClearAsync(Ctx ctx, StepArg a)
    {
        await ctx.SettleAsync();
        await ctx.DismissPopupsAsync();
        var ids = await ListIdsAsync(ctx);
        if (ids.Count == 0)
        {
            ctx.Log("Reports Queue is already empty");
            return;
        }
        ctx.Log($"removing {ids.Count} report(s) from the Reports Queue: {string.Join(", ", ids)}");
        await TickAllAsync(ctx, ids);
        var left = await QueueUi.RemoveSelectedAsync(ctx, ids, () => ListIdsAsync(ctx));
        ctx.Rt.SeenReports.UnionWith(left);
        ctx.Log(left.Count > 0
            ? $"! could not remove {string.Join(", ", left)}; they will be ignored"
            : "Reports Queue is empty");
    }

    /// <summary>nweaQueueDownload: { report: Achievement Status and Growth, saveAs: "...", timeout, poll }</summary>
    private static async Task QueueDownloadAsync(Ctx ctx, StepArg a)
    {
        var report = ctx.Sub(a["report"]);
        var output = ctx.Sub(a["saveAs"], forPath: true);
        var row = await WaitForReadyRowAsync(ctx, report, a["timeout"].IntOr(QueueTimeoutMs), a["poll"].IntOr(QueuePollMs));
        ctx.Rt.SeenReports.Add(row.Id); // never pick this report again, even if removal fails
        var dl = await ctx.Page.RunAndWaitForDownloadAsync(
            () => RowById(ctx, row.Id).GetByText("Download", new() { Exact = true }).First.ClickAsync(),
            new() { Timeout = DownloadTimeoutMs });
        var saved = await SaveDownload.SaveAsync(dl, output);
        ctx.Rt.Stats.Download();
        ctx.Log($"saved: {saved} (report {row.Id}, NWEA name {dl.SuggestedFilename})");
        await QueueUi.TickAsync(ctx, RowById(ctx, row.Id));
        var left = await QueueUi.RemoveSelectedAsync(ctx, [row.Id], () => ListIdsAsync(ctx));
        if (left.Count > 0)
        {
            throw new PlaybookException($"File saved, but report {row.Id} is still in the Reports Queue. Remove it by hand.");
        }
        ctx.Log($"removed report {row.Id} from the Reports Queue");
    }
}
