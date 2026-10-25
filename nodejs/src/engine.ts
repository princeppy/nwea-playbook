// Runs a list of YAML steps, including nested forEach loops.

import { errorLine } from './logger.ts';
import { dismissPopups, log, SkipSignal, sub, type Ctx, type Step, type StepArg, type StepHandler } from './context.ts';
import { openSource, type OpenedSource } from './sources.ts';
import { fileSteps } from './steps/files.ts';
import { inputSteps } from './steps/input.ts';
import { navigationSteps } from './steps/navigation.ts';
import { nweaSteps } from './steps/nwea.ts';
import { parseSeasons, periodAllowed, type Season } from './term.ts';

const HANDLERS: Readonly<Record<string, StepHandler>> = {
  ...navigationSteps,
  ...inputSteps,
  ...fileSteps,
  ...nweaSteps,
  forEach: (ctx, f) => forEach(ctx, f),
};

const describe = (a: StepArg): string => (a && typeof a === 'object' ? JSON.stringify(a) : String(a ?? ''));

export async function runSteps(ctx: Ctx, steps: readonly Step[]): Promise<void> {
  for (const step of steps) {
    const type = Object.keys(step)[0];
    const handler = HANDLERS[type];
    if (!handler) throw new Error(`Unknown step "${type}". Known steps: ${Object.keys(HANDLERS).join(', ')}`);
    const arg = step[type];
    if (type !== 'forEach') log(ctx, `${type} ${describe(arg)}`);
    await dismissPopups(ctx);
    await handler(ctx, arg);
  }
}

/** An item to visit: its name and which occurrence of that name it is (for duplicate names). */
interface Item {
  readonly name: string;
  readonly occurrence: number;
}

/** Index in `names` of the n-th (0-based) occurrence of `name`, or -1. */
export function nthIndexOf(names: readonly string[], name: string, occurrence: number): number {
  let seen = -1;
  return names.findIndex((n) => n === name && ++seen === occurrence);
}

/** The items to visit: drop blanks, skip: names and (with onlySeasons:) untested seasons. */
export function selectItems(names: readonly string[], skip: ReadonlySet<string>, seasons?: readonly Season[]): Item[] {
  const counts = new Map<string, number>();
  return names
    .filter((n) => !!n && !skip.has(n) && (!seasons || periodAllowed(n, seasons)))
    .map((name) => {
      const occurrence = counts.get(name) ?? 0;
      counts.set(name, occurrence + 1);
      return { name, occurrence };
    });
}

/**
 * forEach: open the item list once to learn the names, then visit each name in turn.
 * Every visit after the first runs `before` again and re-opens the list, then picks the
 * item by name, so a list that changes order cannot shift what is picked.
 */
async function forEach(ctx: Ctx, f: StepArg): Promise<void> {
  if (typeof f?.as !== 'string') throw new Error('forEach needs "as: <name>"');
  const skip = new Set<string>((f.skip ?? []).map(String));
  const seasons = f.onlySeasons ? parseSeasons(sub(ctx, f.onlySeasons)) : undefined;
  const inner: Ctx = { ...ctx, depth: ctx.depth + 1 };

  await runSteps(inner, f.before ?? []);
  let source: OpenedSource | undefined = await openSource(ctx, f);
  const items = selectItems(source.names, skip, seasons).slice(0, f.limit ?? Infinity);
  if (seasons) {
    source.names
      .filter((n) => n && !periodAllowed(n, seasons))
      .forEach((n) => log(ctx, `- ${f.as} "${n}" not downloaded (season not in ${seasons.join(', ')})`));
  }
  items
    .filter((it) => it.occurrence === 1)
    .forEach((it) => log(ctx, `! ${f.as} "${it.name}" appears more than once; file names may collide`));
  if (items.length === 0) {
    await source.abandon();
    await runSteps(inner, f.close ?? []);
    return;
  }

  for (const [i, it] of items.entries()) {
    const item: Ctx = {
      ...inner,
      vars: { ...ctx.vars, [f.as]: it.name, [`${f.as}_name`]: stripNotes(it.name, f.strip) },
      pathVars: new Set([...ctx.pathVars, f.as, `${f.as}_name`]),
    };
    try {
      if (!source) {
        await runSteps(inner, f.before ?? []);
        source = await openSource(ctx, f);
      }
      const idx = nthIndexOf(source.names, it.name, it.occurrence);
      const opened = source;
      source = undefined; // the next item re-opens the list
      if (idx < 0) {
        await opened.abandon();
        throw new Error(`"${it.name}" is no longer in the list`);
      }
      await opened.pick(idx);
      log(ctx, `> ${f.as} = ${it.name}  (${i + 1} of ${items.length})`);
      await runSteps(item, f.do ?? []);
    } catch (e: unknown) {
      source = undefined;
      if (e instanceof SkipSignal) {
        ctx.rt.stats.skip(e.reason);
        continue;
      }
      if (f.onError !== 'continue') throw e;
      ctx.rt.stats.fail(`${f.as} "${it.name}"`, errorLine(e));
      log(ctx, `x ${f.as} "${it.name}" failed: ${errorLine(e)}`);
      await ctx.rt.shot(ctx.page, `${f.as}-${it.name}`);
      try {
        await runSteps(item, f.recover ?? ctx.rt.recover);
      } catch (re: unknown) {
        log(ctx, `x recovery after "${it.name}" also failed: ${errorLine(re)} (the next item starts fresh)`);
      }
    }
  }
}

const stripNotes = (name: string, strip: unknown): string =>
  ((strip ?? []) as unknown[]).reduce<string>((t, s) => t.split(String(s)).join('').trim(), name);
