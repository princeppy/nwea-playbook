// Playbook runner (C#) v1.5.0 (2026-10-25)
// A port of the Node.js runner (../nodejs/run.ts, v1.5.0) that runs the same YAML playbooks
// with the same steps, inputs, log lines, summary and exit codes.
//
// Exit codes: 0 all good, 1 stopped by an error, 2 finished but some items failed.

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PlaybookRunner;

/// <summary>The playbook, inputs and secrets, all checked before a browser opens.</summary>
internal sealed record Setup(StepArg Playbook, IReadOnlyDictionary<string, string> Vars, IReadOnlyDictionary<string, string> Secrets);

internal static partial class Program
{
    private const string RunnerVersion = "1.5.0";
    private const int DefaultTimeoutMs = 20_000;
    private const string InstallBrowsers = "--install-browsers";

    [GeneratedRegex(@"[^\w.-]+")]
    private static partial Regex UnsafeLabelChars();

    private static async Task<int> Main(string[] argv)
    {
        if (argv is [InstallBrowsers]) return Microsoft.Playwright.Program.Main(["install", "chromium"]);
        var args = CliArgs.Parse(argv);
        if (args.File is null)
        {
            Console.Error.WriteLine(CliArgs.Usage);
            return 1;
        }
        Setup setup;
        try
        {
            setup = Load(args.File, args.Vars);
        }
        catch (Exception e) when (e is PlaybookException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"FAILED: {RunLog.ErrorLine(e)}");
            return 1;
        }
        return await RunAsync(args, setup);
    }

    /// <summary>Read the playbook, inputs and secrets. Any problem throws before a browser opens.</summary>
    internal static Setup Load(string file, IReadOnlyDictionary<string, string> cli, string envFile = ".env")
    {
        var env = DotEnv.Load(Path.GetFullPath(envFile)); // no .env: secrets may still come from the CLI or environment
        var pb = YamlLoader.Parse(File.ReadAllText(file));
        if (!pb["steps"].IsList) throw new PlaybookException($"{file} has no \"steps:\" list");
        var specs = Secrets.NormalizeSpecs(pb["secrets"]);
        var (values, missing) = Secrets.Resolve(specs, cli, env);
        if (missing.Count > 0)
        {
            throw new PlaybookException(
                $"Missing secret(s): {string.Join(", ", missing)}. Add NAME=\"...\" to .env in this folder.");
        }
        var vars = new Dictionary<string, string>
        {
            ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        foreach (var (k, v) in Params.Resolve(pb["params"], Secrets.WithoutSecretArgs(cli, specs), env)) vars[k] = v;
        return new Setup(pb, vars, values);
    }

    private static async Task<int> RunAsync(CliArgs args, Setup s)
    {
        var mask = Secrets.MakeMasker(s.Secrets.Values);
        var runDir = Path.GetFullPath(Path.Combine(
            "runs", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH-mm-ss-fff'Z'", CultureInfo.InvariantCulture)));
        Directory.CreateDirectory(runDir);
        var log = new RunLog(Path.Combine(runDir, "run.log"), mask);
        var stats = new StatsRecorder();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = !args.Headed, SlowMo = args.SlowMo });
        var context = await browser.NewContextAsync(new()
        {
            AcceptDownloads = true,
            ViewportSize = new() { Width = 1440, Height = 900 },
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(DefaultTimeoutMs);

        async Task Shot(IPage p, string label)
        {
            var file = Path.Combine(runDir, $"{UnsafeLabelChars().Replace(label, "_")}.png");
            await Quietly.Run(p.ScreenshotAsync(new() { Path = file, FullPage = true }));
            log.Info($"screenshot: {file}");
        }

        var rt = new Runtime
        {
            Log = log,
            Stats = stats,
            Network = new NetworkTracker(page),
            Dismiss = s.Playbook["dismiss"].Items,
            Recover = s.Playbook["recover"].Items,
            Secret = name => s.Secrets.TryGetValue(name, out var v)
                ? v
                : throw new PlaybookException($"Secret \"{name}\" is not listed under \"secrets:\" in the playbook."),
            Shot = Shot,
            Run = Engine.RunStepsAsync,
        };
        var ctx = new Ctx(page, s.Vars, new HashSet<string>(), 0, rt);

        log.Info($"Runner v{RunnerVersion} (C#)");
        log.Info($"Playbook: {(s.Playbook["name"].Truthy ? s.Playbook["name"].Text : args.File)}");
        log.Info($"Log: {log.File}");
        var fatal = false;
        try
        {
            await Engine.RunStepsAsync(ctx, s.Playbook["steps"].Items);
            log.Info("Finished");
        }
        catch (SkipSignal e)
        {
            log.Info($"Stopped early: {e.Reason}");
        }
        catch (Exception e)
        {
            fatal = true;
            log.Error($"FAILED: {RunLog.ErrorLine(e)}");
            if (page.Url != "about:blank") await Shot(page, "fatal");
        }
        finally
        {
            foreach (var line in stats.Value.FormatSummary()) log.Info(line);
        }
        if (args.Keep)
        {
            log.Info("Browser left open. Press Ctrl+C to exit.");
            await Task.Delay(Timeout.Infinite);
        }
        if (fatal) return 1;
        return stats.Value.Failures.Count > 0 ? 2 : 0;
    }
}
