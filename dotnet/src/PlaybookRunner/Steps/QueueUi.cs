// Clicking around the NWEA Reports Queue: ticking rows and removing them.
//
// The queue's checkboxes are custom-drawn: the real <input> is hidden, so a plain
// Check() times out (seen on the live site, 25 Oct 2026). We try Check() briefly,
// then click the box drawn in the row's first cell, and confirm with the page's
// "N Reports Selected" counter.

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace PlaybookRunner.Steps;

public static partial class QueueUi
{
    private const int CheckTimeoutMs = 3_000;
    private const int CounterReadMs = 2_000;
    private const int ConfirmWaitMs = 3_000;
    private const int RemoveTimeoutMs = 30_000;

    [GeneratedRegex("^(Remove|Yes|OK|Confirm)$", RegexOptions.IgnoreCase)]
    private static partial Regex ConfirmButton();

    [GeneratedRegex(@"(\d+)\s+Reports?\s+Selected", RegexOptions.IgnoreCase)]
    private static partial Regex SelectedCounter();

    /// <summary>The number in "N Reports Selected", or -1 if the counter is not on the page.</summary>
    public static async Task<int> SelectedCountAsync(IPage page)
    {
        var text = await Quietly.Value(
            page.GetByText(SelectedCounter()).First.InnerTextAsync(new() { Timeout = CounterReadMs }), "");
        var m = SelectedCounter().Match(text);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
    }

    /// <summary>Tick the checkbox in <paramref name="row"/>. Throws if the selection counter does not go up.</summary>
    public static async Task TickAsync(Ctx ctx, ILocator row)
    {
        await ctx.DismissPopupsAsync();
        var before = await SelectedCountAsync(ctx.Page);
        var box = row.GetByRole(AriaRole.Checkbox).First;
        async Task<bool> Ticked() =>
            before < 0 ? await Quietly.Value(box.IsCheckedAsync(), false) : await SelectedCountAsync(ctx.Page) > before;

        await Quietly.Run(box.CheckAsync(new() { Timeout = CheckTimeoutMs }));
        if (await Ticked()) return;
        await Quietly.Run(row.Locator("td, th").First.ClickAsync(new() { Timeout = CheckTimeoutMs }));
        await ctx.Page.WaitForTimeoutAsync(300);
        if (await Ticked()) return;
        await Quietly.Run(box.CheckAsync(new() { Force = true, Timeout = CheckTimeoutMs }));
        if (await Ticked()) return;
        throw new PlaybookException("Could not tick the Reports Queue checkbox");
    }

    /// <summary>
    /// Click Remove Selected (accepting a browser or on-page confirmation), then wait until
    /// none of <paramref name="ids"/> is listed. Returns the ids still listed after the wait.
    /// </summary>
    public static async Task<IReadOnlyList<long>> RemoveSelectedAsync(
        Ctx ctx,
        IReadOnlyList<long> ids,
        Func<Task<IReadOnlyList<long>>> listIds)
    {
        var page = ctx.Page;
        page.Dialog += Accept;
        try
        {
            await page.GetByText("Remove Selected", new() { Exact = true }).First.ClickAsync();
            var confirm = page.GetByRole(AriaRole.Dialog)
                .GetByRole(AriaRole.Button, new() { NameRegex = ConfirmButton() })
                .First;
            if (await Quietly.Run(confirm.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = ConfirmWaitMs })))
            {
                await confirm.ClickAsync();
            }
        }
        finally
        {
            page.Dialog -= Accept;
        }

        var clock = Stopwatch.StartNew();
        IReadOnlyList<long> left = ids;
        while (clock.ElapsedMilliseconds < RemoveTimeoutMs)
        {
            await ctx.SettleAsync();
            var listed = (await listIds()).ToHashSet();
            left = ids.Where(listed.Contains).ToList();
            if (left.Count == 0) return [];
            await page.WaitForTimeoutAsync(1_000);
        }
        return left;
    }

    /// <summary>Accept a browser confirm(); the page may already have closed it.</summary>
    private static void Accept(object? sender, IDialog dialog) => _ = Quietly.Run(dialog.AcceptAsync());
}
