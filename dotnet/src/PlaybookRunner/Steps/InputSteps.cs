// Steps that act on the page: click, fill, choose, setAllSelects, clickIfEnabled, clickIfVisible.

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PlaybookRunner.Steps;

public static class InputSteps
{
    private const int EnabledCheckMs = 3_000;
    private const int IfVisibleTimeoutMs = 3_000;
    private const int MaxSelectPasses = 3;

    private const string SelectTextsJs =
        "s => ({ options: Array.from(s.options).map(o => o.text), selected: s.selectedOptions[0]?.text ?? '' })";

    public static IReadOnlyDictionary<string, StepHandler> All { get; } = new Dictionary<string, StepHandler>
    {
        ["click"] = (ctx, a) => ctx.Locate(a).ClickAsync(),
        ["fill"] = (ctx, a) => ctx.Locate(a).FillAsync(a["secret"].Truthy ? ctx.Rt.Secret(a["secret"].Text) : ctx.Sub(a["value"])),
        ["choose"] = (ctx, a) => Locators.ChooseAsync(ctx.Page, a["in"], ctx.Sub(a["option"]), ctx.Vars),
        ["setAllSelects"] = SetAllSelectsAsync,
        ["clickIfEnabled"] = ClickIfEnabledAsync,
        ["clickIfVisible"] = ClickIfVisibleAsync,
    };

    private sealed record SelectTexts(IReadOnlyList<string> Options, string Selected);

    private sealed record SelectPass(int Matched, int Changed);

    private static async Task<SelectTexts> ReadSelectAsync(ILocator sel)
    {
        var json = await sel.EvaluateAsync<JsonElement>(SelectTextsJs);
        var options = json.GetProperty("options").EnumerateArray().Select(o => o.GetString() ?? "").ToList();
        return new SelectTexts(options, json.GetProperty("selected").GetString() ?? "");
    }

    /// <summary>One pass over the page's selects; returns how many matched and how many it had to change.</summary>
    private static async Task<SelectPass> SetSelectsOnceAsync(Ctx ctx, Regex pattern, string value)
    {
        var selects = ctx.Page.Locator("select");
        var matched = 0;
        var changed = 0;
        for (var i = 0; i < await selects.CountAsync(); i++)
        {
            var sel = selects.Nth(i); // re-queried each time: an AJAX refresh may replace the elements
            var (options, selected) = await ReadSelectAsync(sel);
            if (!options.Any(o => pattern.IsMatch(TextUtil.Normalize(o)))) continue;
            matched++;
            var idx = TextUtil.MatchOptionIndex(options, value);
            if (idx < 0)
            {
                throw new PlaybookException(
                    $"\"{value}\" is not offered. Options: {string.Join(" | ", options.Select(TextUtil.Normalize))}");
            }
            if (TextUtil.Normalize(selected) == TextUtil.Normalize(options[idx])) continue;
            await sel.SelectOptionAsync(new SelectOptionValue { Index = idx });
            changed++;
            ctx.Log($"set dropdown {i + 1} to \"{TextUtil.Normalize(options[idx])}\"");
            await ctx.SettleAsync();
        }
        return new SelectPass(matched, changed);
    }

    /// <summary>
    /// setAllSelects: { optionMatches: '^(Fall|Spring) \d{4}-\d{4}', option: "${term}", min: 2 }
    /// Sets every native &lt;select&gt; that offers an option matching optionMatches to option.
    /// Used for the ASG page, where Term Rostered and Term Tested are both term dropdowns.
    /// </summary>
    private static async Task SetAllSelectsAsync(Ctx ctx, StepArg a)
    {
        var value = ctx.Sub(a["option"]);
        var pattern = new Regex(a["optionMatches"].Text);
        var min = a["min"].IntOr(1);
        // Repeat until a pass changes nothing: changing one dropdown can reset another.
        for (var pass = 1; pass <= MaxSelectPasses; pass++)
        {
            var (matched, changed) = await SetSelectsOnceAsync(ctx, pattern, value);
            if (matched < min)
            {
                throw new PlaybookException($"Expected at least {min} dropdown(s) matching {pattern}, found {matched}.");
            }
            if (changed == 0) return;
        }
        throw new PlaybookException($"Dropdowns kept changing; could not set them all to \"{value}\".");
    }

    private static async Task ClickIfEnabledAsync(Ctx ctx, StepArg a)
    {
        var t = ctx.Locate(a["target"]).First;
        var enabled = await t.CountAsync() > 0
            && await Quietly.Value(t.IsEnabledAsync(new() { Timeout = EnabledCheckMs }), false);
        if (enabled) await t.ClickAsync();
        else if (a["otherwise"].Truthy) await ctx.Locate(a["otherwise"]).ClickAsync();
    }

    /// <summary>clickIfVisible: { target: ..., timeout: 3000 } -- click only if it shows up in time.</summary>
    private static async Task ClickIfVisibleAsync(Ctx ctx, StepArg a)
    {
        var target = a["target"].IsNull ? a : a["target"];
        try
        {
            var shown = await ctx.WaitVisibleAsync(target, a["timeout"].IntOr(IfVisibleTimeoutMs));
            await shown.ClickAsync();
        }
        catch (PlaybookException)
        {
            // not shown in time: nothing to click
        }
    }
}
