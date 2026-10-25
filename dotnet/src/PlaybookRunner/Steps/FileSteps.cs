// Steps about files and skipping: download, skipIfExists, skipIfText.

using System.Text.RegularExpressions;

namespace PlaybookRunner.Steps;

public static class FileSteps
{
    private const int DownloadTimeoutMs = 60_000;
    private const int TextTimeoutMs = 30_000;

    public const string SkipExists = "already downloaded";

    public static IReadOnlyDictionary<string, StepHandler> All { get; } = new Dictionary<string, StepHandler>
    {
        ["download"] = DownloadAsync,
        ["skipIfExists"] = SkipIfExistsAsync,
        ["skipIfText"] = SkipIfTextAsync,
    };

    /// <summary>download: { click: &lt;locator&gt;, saveAs: "...", timeout: 60000 }</summary>
    private static async Task DownloadAsync(Ctx ctx, StepArg a)
    {
        var dl = await ctx.Page.RunAndWaitForDownloadAsync(
            () => ctx.Locate(a["click"]).ClickAsync(),
            new() { Timeout = a["timeout"].IntOr(DownloadTimeoutMs) });
        var saved = await SaveDownload.SaveAsync(dl, ctx.Sub(a["saveAs"], forPath: true));
        ctx.Rt.Stats.Download();
        ctx.Log($"saved: {saved}");
    }

    private static async Task SkipIfExistsAsync(Ctx ctx, StepArg a)
    {
        var path = Path.GetFullPath(ctx.Sub(a["file"], forPath: true));
        if (!File.Exists(path)) return;
        ctx.Log($"skip, already downloaded: {path}");
        await ctx.Rt.Run(ctx, a["cleanup"].Items);
        throw new SkipSignal(SkipExists);
    }

    /// <summary>
    /// skipIfText: { target: &lt;locator&gt;, matches: '^0 out of', reason: no scored students, cleanup: [...] }
    /// Reads the target's text and skips the current loop item when it matches.
    /// </summary>
    private static async Task SkipIfTextAsync(Ctx ctx, StepArg a)
    {
        var el = await ctx.WaitVisibleAsync(a["target"], a["timeout"].IntOr(TextTimeoutMs));
        var text = TextUtil.Normalize(await el.InnerTextAsync());
        if (!new Regex(ctx.Sub(a["matches"])).IsMatch(text)) return;
        var reason = a["reason"].Truthy ? a["reason"].Text : "text matched";
        ctx.Log($"skip, {reason}: \"{text}\"");
        await ctx.Rt.Run(ctx, a["cleanup"].Items);
        throw new SkipSignal(reason);
    }
}
