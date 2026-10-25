// Runs the engine against small local pages in a real (headless) Chromium.
// Pages are served from memory with RouteAsync, so nothing touches the network.
// The steps are written in YAML, exactly as they would appear in a playbook.

namespace PlaybookRunner.Tests;

public class EngineIntegrationTests(BrowserFixture fx) : IClassFixture<BrowserFixture>
{
    private const string AsgSteps = """
        - checkTerm: { term: "${term}", seasons: "${testedSeasons}" }
        - goto: http://test.local/queue
        - nweaQueueClear: {}
        - forEach:
            as: period
            radiosOf: { css: 'input[type="radio"][name$=":growthComparisonPeriod"]' }
            onlySeasons: "${testedSeasons}"
            before:
              - goto: http://test.local/asg
              - setAllSelects: { optionMatches: '^(Fall|Winter|Spring|Summer) \d{4}-\d{4}', option: "${term}", min: 2 }
            do:
              - skipIfExists: { file: "${dir}/ASG - ${period}.csv" }
              - click: { any: [ { role: button, name: Create Spreadsheet }, { text: Create Spreadsheet, exact: true } ] }
              - waitForUrl: { url: "**/queue**", timeout: 5000 }
              - nweaQueueDownload: { report: Achievement Status and Growth, saveAs: "${dir}/ASG - ${period}.csv", poll: 200, timeout: 5000 }
        """;

    private Task<Harness> SetupAsync(Dictionary<string, string>? vars = null) => Harness.CreateAsync(fx, vars);

    private static Dictionary<string, string> Spring() =>
        new() { ["term"] = "Spring 2025-2026", ["testedSeasons"] = "Fall, Spring" };

    [Fact]
    public async Task ASG_loop_sets_both_term_dropdowns_skips_untested_seasons_downloads_from_the_queue()
    {
        var h = await SetupAsync(Spring());
        // Two stale reports from earlier runs sit in the queue; they are cleared before any request.
        await h.Page.GotoAsync("http://test.local/queue");
        await h.Page.EvaluateAsync(
            "() => sessionStorage.setItem('reports', JSON.stringify([{ id: 183000001, readyAt: 0 }, { id: 183000002, readyAt: 0 }]))");
        await h.Run(AsgSteps);

        string[] kept =
        [
            "Spring 2026 - Spring 2027 Growth Projection Report",
            "Fall 2025 - Spring 2026 Growth Summary Report",
            "Spring 2025 - Spring 2026 Growth Summary Report",
        ];
        Assert.All(kept, p => Assert.True(File.Exists(Path.Combine(fx.Dir, $"ASG - {p}.csv")), p));
        Assert.False(File.Exists(Path.Combine(fx.Dir, "ASG - Winter 2026 - Spring 2026 Growth Summary Report.csv")));
        Assert.Equal("id,score\n1,200\n", await File.ReadAllTextAsync(Path.Combine(fx.Dir, $"ASG - {kept[0]}.csv"), TestContext.Current.CancellationToken));
        Assert.Equal(3, h.Stats.Value.Downloaded);
        Assert.Contains(h.Log.Lines, l => l.Contains("dismissed pop-up"));
        Assert.Contains(h.Log.Lines, l => l.Contains("set dropdown") && l.Contains("Spring 2025-2026"));
        Assert.Contains(h.Log.Lines, l => l.Contains("removed report"));
        Assert.Contains(h.Log.Lines, l => l.Contains("removing 2 report(s) from the Reports Queue: 183000001, 183000002"));
        Assert.Contains(h.Log.Lines, l => l.Contains("Reports Queue is empty"));
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("report 18300000"));

        // A second run skips everything that already exists.
        var again = await SetupAsync(Spring());
        await again.Run(AsgSteps);
        Assert.Equal([KeyValuePair.Create("already downloaded", 3)], again.Stats.Value.Skipped);
    }

    [Fact]
    public async Task CheckTerm_stops_a_run_with_a_season_the_school_does_not_test()
    {
        var h = await SetupAsync(new() { ["term"] = "Winter 2026-2027", ["testedSeasons"] = "Fall, Spring" });
        var e = await Assert.ThrowsAsync<PlaybookException>(() => h.Run("""- checkTerm: { term: "${term}", seasons: "${testedSeasons}" }"""));
        Assert.Contains("only tests in Fall and Spring", e.Message);
    }

    [Fact]
    public async Task SkipIfText_skips_a_class_with_0_scored_students_and_downloads_the_others()
    {
        var h = await SetupAsync();
        await h.Run("""
            - forEach:
                as: course
                onError: continue
                radiosOf: { css: 'input[name="course"]' }
                before: [ { goto: http://test.local/class } ]
                do:
                  - waitForIdle: 2000
                  - skipIfText: { target: { regex: '\d+ out of \d+ students? ha(s|ve) an official RIT score' }, matches: '^0 out of', reason: no scored students }
                  - download: { click: { testid: downloadCSVButton }, saveAs: "${dir}/CP - ${course}.csv" }
            """);
        Assert.True(File.Exists(Path.Combine(fx.Dir, "CP - Reading.csv")));
        Assert.False(File.Exists(Path.Combine(fx.Dir, "CP - Algebra 1.csv")));
        Assert.Equal(1, h.Stats.Value.Downloaded);
        Assert.Equal([KeyValuePair.Create("no scored students", 1)], h.Stats.Value.Skipped);
        Assert.Empty(h.Stats.Value.Failures);
    }

    [Fact]
    public async Task Choose_picks_Most_Recent_options_and_filters_long_lists_by_typing()
    {
        var h = await SetupAsync(new() { ["term"] = "Fall 2026-2027" });
        await h.Run("""
            - goto: http://test.local/combo
            - choose: { in: { field: Term Tested }, option: "${term}" }
            """);
        Assert.Equal("term=Fall 2026-2027 (Most Recent)", await h.Page.Locator("#chosen").TextContentAsync());
        await h.Run("- choose: { in: { field: Instructor }, option: 'Daly, Louise' }");
        Assert.Equal("teacher=Daly, Louise", await h.Page.Locator("#chosen").TextContentAsync());
        var e = await Assert.ThrowsAsync<PlaybookException>(() => h.Run("- choose: { in: { field: Instructor }, option: Nobody }"));
        Assert.Contains("\"Nobody\" is not in the list", e.Message);
    }

    [Fact]
    public async Task ForEach_over_a_dropdown_visits_every_option()
    {
        var h = await SetupAsync();
        await h.Run("""
            - goto: http://test.local/combo
            - forEach: { as: teacher, optionsOf: { field: Instructor }, skip: ['AbuFayad, Hiba'], do: [] }
            """);
        Assert.Equal(
            ["> teacher = Abbasi, Nazneen  (1 of 2)", "> teacher = Daly, Louise  (2 of 2)"],
            h.Log.Lines.Where(l => l.Contains("> teacher =")).Select(l => l.Trim()));
    }

    [Fact]
    public async Task Unknown_steps_fail_with_a_helpful_message()
    {
        var h = await SetupAsync();
        var e = await Assert.ThrowsAsync<PlaybookException>(() => h.Run("- clik: x"));
        Assert.Contains("Unknown step \"clik\"", e.Message);
    }
}
