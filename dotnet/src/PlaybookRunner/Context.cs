// What every step receives: the page, the current ${vars}, and the shared runtime.

using System.Globalization;
using Microsoft.Playwright;

namespace PlaybookRunner;

/// <summary>A step type: receives the context and the step's YAML argument.</summary>
public delegate Task StepHandler(Ctx ctx, StepArg arg);

public sealed class Runtime
{
    public required IRunLog Log { get; init; }
    public required StatsRecorder Stats { get; init; }
    public required INetworkQuiet Network { get; init; }
    public required IReadOnlyList<StepArg> Dismiss { get; init; }
    public required IReadOnlyList<StepArg> Recover { get; init; }

    /// <summary>Reports Queue ids already present or downloaded; never picked as "the new report".</summary>
    public HashSet<long> SeenReports { get; } = [];

    public required Func<string, string> Secret { get; init; }
    public required Func<IPage, string, Task> Shot { get; init; }
    public required Func<Ctx, IReadOnlyList<StepArg>, Task> Run { get; init; }
}

/// <param name="PathVars">Loop variables: cleaned for file names when used in a path.</param>
public sealed record Ctx(
    IPage Page,
    IReadOnlyDictionary<string, string> Vars,
    IReadOnlySet<string> PathVars,
    int Depth,
    Runtime Rt);

/// <summary>Thrown to skip the rest of the current loop item; the reason is counted in the summary.</summary>
public sealed class SkipSignal(string reason) : Exception($"skip: {reason}")
{
    public string Reason { get; } = reason;
}

public static class CtxExtensions
{
    private const int PollMs = 250;
    private const int SettleTimeoutMs = 15_000;
    private const int DismissClickMs = 2_000;

    /// <summary>Milliseconds as seconds for messages, like JavaScript prints them: 15000 -> "15", 300 -> "0.3".</summary>
    public static string Seconds(int ms) => (ms / 1000.0).ToString(CultureInfo.InvariantCulture);

    public static string Sub(this Ctx ctx, StepArg text, bool forPath = false) =>
        Vars.Substitute(text.Text, ctx.Vars, forPath ? ctx.PathVars : null);

    public static ILocator Locate(this Ctx ctx, StepArg spec) => Locators.Find(ctx.Page, spec, ctx.Vars);

    public static void Log(this Ctx ctx, string message) => ctx.Rt.Log.Info(message, ctx.Depth);

    public static Task<bool> IsVisibleNowAsync(ILocator l) => Quietly.Value(l.IsVisibleAsync(), false);

    /// <summary>Wait for the page's AJAX requests to finish. Never fails: logs and returns false on timeout.</summary>
    public static async Task<bool> SettleAsync(this Ctx ctx, int timeoutMs = SettleTimeoutMs)
    {
        var quiet = await ctx.Rt.Network.WaitQuietAsync(timeoutMs);
        if (!quiet) ctx.Log($"(page still busy after {Seconds(timeoutMs)}s, continuing)");
        return quiet;
    }

    /// <summary>Click any optional pop-up listed under "dismiss:" in the playbook (e.g. a System Alert).</summary>
    public static async Task DismissPopupsAsync(this Ctx ctx)
    {
        foreach (var spec in ctx.Rt.Dismiss)
        {
            var popup = ctx.Locate(spec).First;
            if (!await IsVisibleNowAsync(popup)) continue;
            ctx.Log($"dismissed pop-up {spec.Describe()}");
            await Quietly.Run(popup.ClickAsync(new() { Timeout = DismissClickMs }));
        }
    }

    /// <summary>Wait until <paramref name="spec"/> is visible, dismissing pop-ups while waiting.</summary>
    public static async Task<ILocator> WaitVisibleAsync(this Ctx ctx, StepArg spec, int timeoutMs)
    {
        var target = ctx.Locate(spec).First;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            await ctx.DismissPopupsAsync();
            if (await IsVisibleNowAsync(target)) return target;
            if (DateTime.UtcNow > deadline)
            {
                throw new PlaybookException($"Timed out after {timeoutMs} ms waiting for {spec.Describe()}");
            }
            await ctx.Page.WaitForTimeoutAsync(PollMs);
        }
    }
}

/// <summary>
/// Optional browser actions whose failure is expected and harmless (a pop-up that vanished,
/// a checkbox that cannot be checked directly). Only Playwright errors and timeouts are swallowed;
/// in .NET, Playwright reports a timeout as System.TimeoutException, not PlaywrightException.
/// </summary>
public static class Quietly
{
    private static bool IsBrowserError(Exception e) => e is PlaywrightException or TimeoutException;

    /// <summary>Run an action; true if it worked, false if Playwright reported an error or timed out.</summary>
    public static async Task<bool> Run(Task action)
    {
        try
        {
            await action;
            return true;
        }
        catch (Exception e) when (IsBrowserError(e))
        {
            return false;
        }
    }

    /// <summary>The action's result, or <paramref name="fallback"/> if Playwright reported an error or timed out.</summary>
    public static async Task<T> Value<T>(Task<T> action, T fallback)
    {
        try
        {
            return await action;
        }
        catch (Exception e) when (IsBrowserError(e))
        {
            return fallback;
        }
    }
}
