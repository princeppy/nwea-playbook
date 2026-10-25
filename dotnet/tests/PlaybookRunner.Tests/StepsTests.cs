// The less-used steps and forEach options: optional waits, conditional clicks, fill,
// screenshots, onError: continue with recovery, close:, limit:, strip: and input errors.

namespace PlaybookRunner.Tests;

public class StepsTests(BrowserFixture fx) : IClassFixture<BrowserFixture>
{
    private const string Buttons = """
        <button id="off" disabled>Update</button>
        <button id="cancel" onclick="document.getElementById('out').textContent = 'cancelled'">Cancel</button>
        <button id="late" hidden onclick="document.getElementById('out').textContent = 'late'">Late</button>
        <input id="name">
        <p id="out"></p>
        <script>setTimeout(() => { document.getElementById('late').hidden = false; }, 300);</script>
        """;

    private async Task<Harness> WithButtonsAsync(Dictionary<string, string>? vars = null)
    {
        var h = await Harness.CreateAsync(fx, vars);
        await h.Page.SetContentAsync(Buttons);
        return h;
    }

    private static async Task<string?> OutAsync(Harness h) => await h.Page.Locator("#out").TextContentAsync();

    [Fact]
    public async Task WaitFor_optional_logs_and_continues_otherwise_it_fails()
    {
        var h = await WithButtonsAsync();
        await h.Run("- waitFor: { css: '#nope', timeout: 300, optional: true }");
        Assert.Contains("(not seen within 0.3s, continuing)", h.Log.Lines);
        var e = await Assert.ThrowsAsync<PlaybookException>(() => h.Run("- waitFor: { css: '#nope', timeout: 300 }"));
        Assert.StartsWith("Timed out after 300 ms waiting for {\"css\":\"#nope\"", e.Message);
    }

    [Fact]
    public async Task ClickIfEnabled_clicks_the_fallback_when_the_target_is_disabled()
    {
        var h = await WithButtonsAsync();
        await h.Run("- clickIfEnabled: { target: { css: '#off' }, otherwise: { css: '#cancel' } }");
        Assert.Equal("cancelled", await OutAsync(h));
    }

    [Fact]
    public async Task ClickIfVisible_waits_for_a_late_element_and_ignores_a_missing_one()
    {
        var h = await WithButtonsAsync();
        await h.Run("- clickIfVisible: { target: { css: '#nope' }, timeout: 200 }");
        Assert.Equal("", await OutAsync(h));
        await h.Run("- clickIfVisible: { target: { css: '#late' }, timeout: 3000 }");
        Assert.Equal("late", await OutAsync(h));
    }

    [Fact]
    public async Task Fill_types_a_value_or_a_secret()
    {
        var h = await WithButtonsAsync(new() { ["who"] = "Daly" });
        await h.Run("- fill: { css: '#name', value: 'Ms ${who}' }");
        Assert.Equal("Ms Daly", await h.Page.InputValueAsync("#name"));
        await h.Run("- fill: { css: '#name', secret: NWEA_PASS }");
        Assert.Equal("x", await h.Page.InputValueAsync("#name"));
    }

    [Fact]
    public async Task Goto_sleep_waitForIdle_and_screenshot()
    {
        var h = await Harness.CreateAsync(fx, new() { ["v"] = "a/b" });
        await h.Run("""
            - goto: { url: http://test.local/combo, timeout: 5000 }
            - waitForIdle:
            - sleep: 10
            - screenshot: "label ${v}"
            """);
        Assert.EndsWith("/combo", h.Page.Url);
        Assert.Equal(["label a/b"], h.Shots);
    }

    [Fact]
    public async Task ForEach_onError_continue_counts_the_failure_and_runs_recover()
    {
        var h = await Harness.CreateAsync(fx);
        await h.Run("""
            - goto: http://test.local/combo
            - forEach:
                as: teacher
                optionsOf: { field: Instructor }
                limit: 2
                onError: continue
                do: [ { waitFor: { css: '#nope', timeout: 100 } } ]
                recover: [ { sleep: 1 } ]
            """);
        Assert.Equal(2, h.Stats.Value.Failures.Count);
        Assert.Equal("teacher \"Abbasi, Nazneen\"", h.Stats.Value.Failures[0].Item);
        Assert.Equal(["teacher-Abbasi, Nazneen", "teacher-AbuFayad, Hiba"], h.Shots);
        Assert.Equal(2, h.Log.Lines.Count(l => l == "sleep 1"));
    }

    [Fact]
    public async Task ForEach_logs_a_recovery_that_also_fails_and_moves_on()
    {
        var h = await Harness.CreateAsync(fx);
        await h.Run("""
            - goto: http://test.local/combo
            - forEach:
                as: teacher
                optionsOf: { field: Instructor }
                limit: 1
                onError: continue
                do: [ { waitFor: { css: '#nope', timeout: 100 } } ]
                recover: [ { clik: x } ]
            """);
        Assert.Contains(h.Log.Lines, l => l.StartsWith("x recovery after \"Abbasi, Nazneen\" also failed: Unknown step", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForEach_without_onError_stops_at_the_first_failure()
    {
        var h = await Harness.CreateAsync(fx);
        await Assert.ThrowsAsync<PlaybookException>(() => h.Run("""
            - goto: http://test.local/combo
            - forEach: { as: teacher, optionsOf: { field: Instructor }, do: [ { waitFor: { css: '#nope', timeout: 100 } } ] }
            """));
        Assert.Empty(h.Stats.Value.Failures);
    }

    [Fact]
    public async Task ForEach_strip_gives_a_clean_name_variable()
    {
        var h = await Harness.CreateAsync(fx);
        await h.Run("""
            - goto: http://test.local/combo
            - forEach: { as: teacher, optionsOf: { field: Instructor }, limit: 1, strip: [', Nazneen'], do: [ { screenshot: "${teacher_name}" } ] }
            """);
        Assert.Equal(["Abbasi"], h.Shots);
    }

    [Fact]
    public async Task ForEach_with_nothing_to_visit_runs_close()
    {
        var h = await Harness.CreateAsync(fx);
        await h.Run("""
            - goto: http://test.local/combo
            - forEach:
                as: teacher
                optionsOf: { field: Instructor }
                skip: ['Abbasi, Nazneen', 'AbuFayad, Hiba', 'Daly, Louise']
                close: [ { sleep: 1 } ]
                do: []
            """);
        Assert.Contains("sleep 1", h.Log.Lines);
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("> teacher"));
    }

    [Theory]
    [InlineData("- forEach: { optionsOf: { field: Instructor } }", "forEach needs \"as: <name>\"")]
    [InlineData("- forEach: { as: teacher }", "needs optionsOf (dropdown) or radiosOf")]
    [InlineData("- click: { nope: 1 }", "Unknown locator {\"nope\":1}")]
    [InlineData("- click: { role: nonsense }", "Unknown ARIA role \"nonsense\"")]
    [InlineData("- just a string", "Each step must be a map")]
    public async Task Bad_steps_fail_with_a_helpful_message(string yaml, string message)
    {
        var h = await Harness.CreateAsync(fx);
        await h.Page.GotoAsync("http://test.local/combo");
        var e = await Assert.ThrowsAsync<PlaybookException>(() => h.Run(yaml));
        Assert.Contains(message, e.Message);
    }

    [Theory]
    [InlineData("option: Summer 2030-2031, min: 2", "\"Summer 2030-2031\" is not offered")]
    [InlineData("option: Fall 2026-2027, min: 5", "Expected at least 5 dropdown(s)")]
    public async Task SetAllSelects_reports_bad_input(string args, string message)
    {
        var h = await Harness.CreateAsync(fx);
        await h.Page.GotoAsync("http://test.local/asg");
        var e = await Assert.ThrowsAsync<PlaybookException>(() =>
            h.Run($"- setAllSelects: {{ optionMatches: '^(Fall|Spring) \\d{{4}}-\\d{{4}}', {args} }}"));
        Assert.Contains(message, e.Message);
    }
}
