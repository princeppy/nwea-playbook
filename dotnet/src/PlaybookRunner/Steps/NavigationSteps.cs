// Steps that move around or wait: goto, waitFor, waitForIdle, waitForUrl, sleep, screenshot.

using Microsoft.Playwright;

namespace PlaybookRunner.Steps;

public static class NavigationSteps
{
    private const int GotoTimeoutMs = 60_000;
    private const int WaitForTimeoutMs = 30_000;
    private const int IdleTimeoutMs = 15_000;
    private const int UrlTimeoutMs = 60_000;

    public static IReadOnlyDictionary<string, StepHandler> All { get; } = new Dictionary<string, StepHandler>
    {
        ["goto"] = GotoAsync,
        ["waitFor"] = WaitForAsync,
        ["waitForIdle"] = async (ctx, a) => await ctx.SettleAsync(a.IntOr(IdleTimeoutMs)),
        ["waitForUrl"] = WaitForUrlAsync,
        ["sleep"] = (ctx, a) => ctx.Page.WaitForTimeoutAsync(a.IntOr(0)),
        ["screenshot"] = (ctx, a) => ctx.Rt.Shot(ctx.Page, ctx.Sub(a)),
    };

    /// <summary>goto: https://...  or  { url: https://..., timeout: 90000 }</summary>
    private static async Task GotoAsync(Ctx ctx, StepArg a)
    {
        var url = ctx.Sub(a.IsMap ? a["url"] : a);
        var timeout = a["timeout"].IntOr(GotoTimeoutMs);
        await ctx.Page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = timeout });
    }

    /// <summary>waitFor: &lt;locator&gt;, with optional timeout; optional: true logs instead of failing.</summary>
    private static async Task WaitForAsync(Ctx ctx, StepArg a)
    {
        var timeout = a["timeout"].IntOr(WaitForTimeoutMs);
        try
        {
            await ctx.WaitVisibleAsync(a, timeout);
        }
        catch (PlaybookException) when (a["optional"].Truthy)
        {
            ctx.Log($"(not seen within {CtxExtensions.Seconds(timeout)}s, continuing)");
        }
    }

    /// <summary>waitForUrl: "**/queue**"  or  { url: "**/queue**", timeout: 300000 }</summary>
    private static async Task WaitForUrlAsync(Ctx ctx, StepArg a)
    {
        var url = ctx.Sub(a.IsMap ? a["url"] : a);
        var timeout = a["timeout"].IntOr(UrlTimeoutMs);
        await ctx.Page.WaitForURLAsync(url, new() { Timeout = timeout, WaitUntil = WaitUntilState.DOMContentLoaded });
    }
}
