// Shared set-up for the browser tests: one headless Chromium per test class, the stand-in
// pages served from memory at http://test.local/, and a runtime that records log lines,
// stats and screenshot labels instead of writing files.

using Microsoft.Playwright;
using PlaybookRunner.Tests.Fixtures;

namespace PlaybookRunner.Tests;

public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string Dir { get; } = Directory.CreateTempSubdirectory("playbook-test-").FullName;

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync();
        _playwright?.Dispose();
        Directory.Delete(Dir, recursive: true);
    }
}

internal sealed class TestLog : IRunLog
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines => _lines;
    public string File => "";

    public void Info(string message, int depth = 0) => _lines.Add(message);

    public void Error(string message) => _lines.Add(message);
}

internal sealed record Harness(Ctx Ctx, IPage Page, StatsRecorder Stats, TestLog Log, IReadOnlyList<string> Shots)
{
    public Task Run(string yamlSteps) => Engine.RunStepsAsync(Ctx, YamlLoader.Parse(yamlSteps).Items);

    public static async Task<Harness> CreateAsync(BrowserFixture fx, Dictionary<string, string>? vars = null)
    {
        var context = await fx.Browser.NewContextAsync(new() { AcceptDownloads = true });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(5_000);
        await page.RouteAsync("http://test.local/**", route =>
            Pages.ByPath.TryGetValue(new Uri(route.Request.Url).AbsolutePath, out var body)
                ? route.FulfillAsync(new() { ContentType = "text/html", Body = body })
                : route.FulfillAsync(new() { Status = 404 }));
        var stats = new StatsRecorder();
        var log = new TestLog();
        var shots = new List<string>();
        var rt = new Runtime
        {
            Log = log,
            Stats = stats,
            Network = new NetworkTracker(page),
            Dismiss = YamlLoader.Parse("[ { css: '#alert-ok' } ]").Items,
            Recover = [],
            Secret = _ => "x",
            Shot = (_, label) =>
            {
                shots.Add(label);
                return Task.CompletedTask;
            },
            Run = Engine.RunStepsAsync,
        };
        var allVars = new Dictionary<string, string>(vars ?? []) { ["dir"] = fx.Dir };
        return new Harness(new Ctx(page, allVars, new HashSet<string>(), 0, rt), page, stats, log, shots);
    }
}
