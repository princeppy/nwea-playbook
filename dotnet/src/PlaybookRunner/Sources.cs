// Where a forEach loop gets its items from:
//   optionsOf: <combobox locator>   the options of a dropdown (opened each time)
//   radiosOf:  <radio locator>      a group of radio buttons, named by their <label for=...>

using Microsoft.Playwright;

namespace PlaybookRunner;

/// <param name="Names">Item names, in page order.</param>
/// <param name="Pick">Select the item at an index into <paramref name="Names"/>.</param>
/// <param name="Abandon">Leave without picking (e.g. close the dropdown).</param>
public sealed record OpenedSource(IReadOnlyList<string> Names, Func<int, Task> Pick, Func<Task> Abandon);

public static class Sources
{
    private static async Task<OpenedSource> OpenDropdownAsync(Ctx ctx, StepArg box)
    {
        var opts = await Locators.OpenOptionsAsync(ctx.Page, box, ctx.Vars);
        var names = (await opts.AllInnerTextsAsync()).Select(TextUtil.Normalize).ToList();
        return new OpenedSource(names, i => opts.Nth(i).ClickAsync(), () => ctx.Page.Keyboard.PressAsync("Escape"));
    }

    private static async Task<OpenedSource> OpenRadiosAsync(Ctx ctx, StepArg spec)
    {
        var radios = ctx.Locate(spec);
        await radios.First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        var count = await radios.CountAsync();
        var names = new List<string>();
        for (var i = 0; i < count; i++)
        {
            names.Add(TextUtil.Normalize(await RadioNameAsync(ctx.Page, radios.Nth(i))));
        }
        return new OpenedSource(names, i => radios.Nth(i).ClickAsync(), () => Task.CompletedTask);
    }

    /// <summary>The text of the radio's &lt;label for="id"&gt;, or its value attribute.</summary>
    private static async Task<string> RadioNameAsync(IPage page, ILocator radio)
    {
        var id = await radio.GetAttributeAsync("id");
        if (id is not null)
        {
            var label = page.Locator($"label[for=\"{id.Replace("\"", "\\\"", StringComparison.Ordinal)}\"]").First;
            if (await label.CountAsync() > 0) return await label.InnerTextAsync();
        }
        return await radio.GetAttributeAsync("value") ?? "";
    }

    public static Task<OpenedSource> OpenAsync(Ctx ctx, StepArg f)
    {
        if (f["optionsOf"].Truthy) return OpenDropdownAsync(ctx, f["optionsOf"]);
        if (f["radiosOf"].Truthy) return OpenRadiosAsync(ctx, f["radiosOf"]);
        throw new PlaybookException($"forEach \"{f["as"].Text}\" needs optionsOf (dropdown) or radiosOf (radio buttons)");
    }
}
