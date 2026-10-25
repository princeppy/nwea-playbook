// Runs a list of YAML steps, including nested forEach loops.

using PlaybookRunner.Steps;

namespace PlaybookRunner;

/// <summary>An item to visit: its name and which occurrence of that name it is (for duplicate names).</summary>
public sealed record LoopItem(string Name, int Occurrence);

public static class Engine
{
    private static readonly IReadOnlyDictionary<string, StepHandler> Handlers =
        new[] { NavigationSteps.All, InputSteps.All, FileSteps.All, NweaSteps.All }
            .SelectMany(d => d)
            .Append(KeyValuePair.Create<string, StepHandler>("forEach", ForEachAsync))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    public static async Task RunStepsAsync(Ctx ctx, IReadOnlyList<StepArg> steps)
    {
        foreach (var step in steps)
        {
            if (step.Entries is not [var (type, arg), ..])
            {
                throw new PlaybookException($"Each step must be a map like {{ click: ... }}, got {step.Describe()}");
            }
            if (!Handlers.TryGetValue(type, out var handler))
            {
                throw new PlaybookException($"Unknown step \"{type}\". Known steps: {string.Join(", ", Handlers.Keys)}");
            }
            if (type != "forEach") ctx.Log($"{type} {arg.Describe()}");
            await ctx.DismissPopupsAsync();
            await handler(ctx, arg);
        }
    }

    /// <summary>Index in <paramref name="names"/> of the n-th (0-based) occurrence of <paramref name="name"/>, or -1.</summary>
    public static int NthIndexOf(IReadOnlyList<string> names, string name, int occurrence)
    {
        var seen = -1;
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] == name && ++seen == occurrence) return i;
        }
        return -1;
    }

    /// <summary>The items to visit: drop blanks, skip: names and (with onlySeasons:) untested seasons.</summary>
    public static IReadOnlyList<LoopItem> SelectItems(
        IReadOnlyList<string> names,
        IReadOnlySet<string> skip,
        IReadOnlyList<string>? seasons = null)
    {
        var counts = new Dictionary<string, int>();
        return names
            .Where(n => n.Length > 0 && !skip.Contains(n) && (seasons is null || Terms.PeriodAllowed(n, seasons)))
            .Select(n =>
            {
                var occurrence = counts.GetValueOrDefault(n);
                counts[n] = occurrence + 1;
                return new LoopItem(n, occurrence);
            })
            .ToList();
    }

    /// <summary>
    /// forEach: open the item list once to learn the names, then visit each name in turn.
    /// Every visit after the first runs `before` again and re-opens the list, then picks the
    /// item by name, so a list that changes order cannot shift what is picked.
    /// </summary>
    private static async Task ForEachAsync(Ctx ctx, StepArg f)
    {
        if (!f["as"].IsString) throw new PlaybookException("forEach needs \"as: <name>\"");
        var name = f["as"].Text;
        var skip = f["skip"].Items.Select(s => s.Text).ToHashSet();
        var seasons = f["onlySeasons"].Truthy ? Terms.ParseSeasons(ctx.Sub(f["onlySeasons"])) : null;
        var inner = ctx with { Depth = ctx.Depth + 1 };

        await RunStepsAsync(inner, f["before"].Items);
        OpenedSource? source = await Sources.OpenAsync(ctx, f);
        var items = SelectItems(source.Names, skip, seasons).Take(f["limit"].IntOr(int.MaxValue)).ToList();
        LogFiltered(ctx, name, source.Names, seasons, items);
        if (items.Count == 0)
        {
            await source.Abandon();
            await RunStepsAsync(inner, f["close"].Items);
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var item = inner with
            {
                Vars = new Dictionary<string, string>(ctx.Vars) { [name] = it.Name, [$"{name}_name"] = StripNotes(it.Name, f["strip"]) },
                PathVars = new HashSet<string>(ctx.PathVars) { name, $"{name}_name" },
            };
            try
            {
                if (source is null)
                {
                    await RunStepsAsync(inner, f["before"].Items);
                    source = await Sources.OpenAsync(ctx, f);
                }
                var opened = source;
                source = null; // the next item re-opens the list
                var idx = NthIndexOf(opened.Names, it.Name, it.Occurrence);
                if (idx < 0)
                {
                    await opened.Abandon();
                    throw new PlaybookException($"\"{it.Name}\" is no longer in the list");
                }
                await opened.Pick(idx);
                ctx.Log($"> {name} = {it.Name}  ({i + 1} of {items.Count})");
                await RunStepsAsync(item, f["do"].Items);
            }
            catch (SkipSignal s)
            {
                source = null;
                ctx.Rt.Stats.Skip(s.Reason);
            }
            catch (Exception e) when (f["onError"].Text == "continue")
            {
                source = null;
                await ContinueAfterErrorAsync(ctx, f, name, it, item, e);
            }
        }
    }

    private static void LogFiltered(
        Ctx ctx,
        string name,
        IReadOnlyList<string> names,
        IReadOnlyList<string>? seasons,
        IReadOnlyList<LoopItem> items)
    {
        if (seasons is not null)
        {
            foreach (var n in names.Where(n => n.Length > 0 && !Terms.PeriodAllowed(n, seasons)))
            {
                ctx.Log($"- {name} \"{n}\" not downloaded (season not in {string.Join(", ", seasons)})");
            }
        }
        foreach (var it in items.Where(it => it.Occurrence == 1))
        {
            ctx.Log($"! {name} \"{it.Name}\" appears more than once; file names may collide");
        }
    }

    /// <summary>onError: continue -- count the failure, take a screenshot, run the recovery steps.</summary>
    private static async Task ContinueAfterErrorAsync(Ctx ctx, StepArg f, string name, LoopItem it, Ctx item, Exception e)
    {
        var error = RunLog.ErrorLine(e);
        ctx.Rt.Stats.Fail($"{name} \"{it.Name}\"", error);
        ctx.Log($"x {name} \"{it.Name}\" failed: {error}");
        await ctx.Rt.Shot(ctx.Page, $"{name}-{it.Name}");
        var recover = f["recover"].IsNull ? ctx.Rt.Recover : f["recover"].Items;
        try
        {
            await RunStepsAsync(item, recover);
        }
        catch (Exception re)
        {
            ctx.Log($"x recovery after \"{it.Name}\" also failed: {RunLog.ErrorLine(re)} (the next item starts fresh)");
        }
    }

    private static string StripNotes(string name, StepArg strip) =>
        strip.Items
            .Select(s => s.Text)
            .Where(s => s.Length > 0)
            .Aggregate(name, (t, s) => t.Replace(s, "", StringComparison.Ordinal).Trim());
}
