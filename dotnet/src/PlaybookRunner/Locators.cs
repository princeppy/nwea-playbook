// Turns a YAML locator into a Playwright locator, and picks options from dropdowns.
//
//   "Some text"                      text (substring)
//   { css: ... } { testid: ... }     CSS selector / data-testid
//   { role: button, name: Update }   ARIA role (+ name, exact)
//   { text: ..., exact: true }       text
//   { regex: '\d+ out of \d+' }      text matching a regular expression
//   { endsWith: '| ${course}' }      text ending with exactly this (special characters are literal)
//   { field: Instructor }            first combobox after a label with that text
//   { any: [ ..., ... ] }            the first of several alternatives
//   nth: 2                           pick one of several matches

using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PlaybookRunner;

public static class Locators
{
    private const int OptionsTimeoutMs = 15_000;
    private const int FilterSettleMs = 500;

    /// <summary>Quote a string for XPath.</summary>
    private static string Xq(string s) => s.Contains('"') ? $"'{s}'" : $"\"{s}\"";

    public static ILocator Find(IPage page, StepArg spec, IReadOnlyDictionary<string, string> vars)
    {
        var s = spec.IsString ? new StepArg(new Dictionary<string, object?> { ["text"] = spec.Value }) : spec;
        string T(string key) => Vars.Substitute(s[key].Text, vars);

        ILocator l;
        if (s["any"].IsList) l = s["any"].Items.Select(a => Find(page, a, vars)).Aggregate((a, b) => a.Or(b)).First;
        else if (s["css"].Truthy) l = page.Locator(T("css"));
        else if (s["testid"].Truthy) l = page.GetByTestId(T("testid"));
        else if (s["field"].Truthy)
            l = page.Locator($"xpath=(//*[normalize-space(.)={Xq(T("field"))}]/following::*[@role=\"combobox\"])[1]");
        else if (s["role"].Truthy)
            l = page.GetByRole(
                Role(s["role"].Text),
                s["name"].Truthy ? new() { Name = T("name"), Exact = s["exact"].Truthy } : null);
        else if (s["regex"].Truthy) l = page.GetByText(new Regex(T("regex")));
        else if (s["endsWith"].Truthy) l = page.GetByText(new Regex($@"{Regex.Escape(T("endsWith"))}\s*$"));
        else if (s["text"].Truthy) l = page.GetByText(T("text"), new() { Exact = s["exact"].Truthy });
        else throw new PlaybookException($"Unknown locator {spec.Describe()}");
        return s.Has("nth") ? l.Nth(s["nth"].IntOr(0)) : l;
    }

    private static AriaRole Role(string name) =>
        Enum.TryParse<AriaRole>(name, ignoreCase: true, out var role)
            ? role
            : throw new PlaybookException($"Unknown ARIA role \"{name}\"");

    /// <summary>Click a combobox and wait for its options to show.</summary>
    public static async Task<ILocator> OpenOptionsAsync(IPage page, StepArg box, IReadOnlyDictionary<string, string> vars)
    {
        await Find(page, box, vars).ClickAsync();
        var opts = page.GetByRole(AriaRole.Option);
        await opts.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = OptionsTimeoutMs });
        return opts;
    }

    private static async Task<IReadOnlyList<string>> OptionNamesAsync(ILocator opts) =>
        (await opts.AllInnerTextsAsync()).Select(TextUtil.Normalize).ToList();

    /// <summary>
    /// Pick <paramref name="value"/> in a combobox. Accepts an exact match or the value plus a bracketed
    /// note ("Fall 2026-2027 (Most Recent)"). Types the value to filter long lists.
    /// </summary>
    public static async Task ChooseAsync(IPage page, StepArg box, string value, IReadOnlyDictionary<string, string> vars)
    {
        var opts = await OpenOptionsAsync(page, box, vars);
        var names = await OptionNamesAsync(opts);
        var idx = TextUtil.MatchOptionIndex(names, value);
        if (idx < 0)
        {
            await Find(page, box, vars).FillAsync(value);
            await page.WaitForTimeoutAsync(FilterSettleMs);
            names = await OptionNamesAsync(page.GetByRole(AriaRole.Option));
            idx = TextUtil.MatchOptionIndex(names, value);
        }
        if (idx < 0)
        {
            throw new PlaybookException($"\"{value}\" is not in the list. Options: {string.Join(" | ", names.Take(20))}");
        }
        await page.GetByRole(AriaRole.Option).Nth(idx).ClickAsync();
    }
}
