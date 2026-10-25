// What every step receives: the page, the current ${vars}, and the shared runtime.

import type { Locator, Page } from 'playwright';
import { find, type LocatorSpec } from './locators.ts';
import type { Logger } from './logger.ts';
import type { NetworkTracker } from './network.ts';
import type { StatsRecorder } from './stats.ts';
import { substitute, type Vars } from './vars.ts';

/** One YAML step, e.g. { click: { testid: cancel-button } }. Values come from YAML, so they are loose. */
export type StepArg = any; // eslint-disable-line @typescript-eslint/no-explicit-any
export type Step = Readonly<Record<string, StepArg>>;

export interface Runtime {
  readonly log: Logger;
  readonly stats: StatsRecorder;
  readonly network: NetworkTracker;
  readonly dismiss: readonly LocatorSpec[];
  readonly recover: readonly Step[];
  /** Reports Queue ids already present or downloaded; never picked as "the new report". */
  readonly seenReports: Set<number>;
  secret(name: string): string;
  shot(page: Page, label: string): Promise<void>;
  run(ctx: Ctx, steps: readonly Step[]): Promise<void>;
}

export interface Ctx {
  readonly page: Page;
  readonly vars: Vars;
  /** Loop variables: cleaned for file names when used in a path. */
  readonly pathVars: ReadonlySet<string>;
  readonly depth: number;
  readonly rt: Runtime;
}

export type StepHandler = (ctx: Ctx, arg: StepArg) => Promise<void>;

/** Thrown to skip the rest of the current loop item; `reason` is counted in the summary. */
export class SkipSignal extends Error {
  constructor(readonly reason: string) {
    super(`skip: ${reason}`);
  }
}

const POLL_MS = 250;
const SETTLE_TIMEOUT_MS = 15_000;
const DISMISS_CLICK_MS = 2_000;

export const sub = (ctx: Ctx, text: unknown, forPath = false): string =>
  substitute(text, ctx.vars, forPath ? ctx.pathVars : undefined);

export const locate = (ctx: Ctx, spec: LocatorSpec): Locator => find(ctx.page, spec, ctx.vars);

export const log = (ctx: Ctx, message: string): void => ctx.rt.log.info(message, ctx.depth);

export const isVisibleNow = (l: Locator): Promise<boolean> => l.isVisible().catch(() => false);

/** Wait for the page's AJAX requests to finish. Never fails: logs and returns false on timeout. */
export async function settle(ctx: Ctx, timeoutMs = SETTLE_TIMEOUT_MS): Promise<boolean> {
  const quiet = await ctx.rt.network.waitQuiet(timeoutMs);
  if (!quiet) log(ctx, `(page still busy after ${timeoutMs / 1000}s, continuing)`);
  return quiet;
}

/** Click any optional pop-up listed under "dismiss:" in the playbook (e.g. a System Alert). */
export async function dismissPopups(ctx: Ctx): Promise<void> {
  for (const spec of ctx.rt.dismiss) {
    const popup = locate(ctx, spec).first();
    if (await isVisibleNow(popup)) {
      log(ctx, `dismissed pop-up ${JSON.stringify(spec)}`);
      await popup.click({ timeout: DISMISS_CLICK_MS }).catch(() => {});
    }
  }
}

/** Wait until `spec` is visible, dismissing pop-ups while waiting. */
export async function waitVisible(ctx: Ctx, spec: LocatorSpec, timeoutMs: number): Promise<Locator> {
  const target = locate(ctx, spec).first();
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    await dismissPopups(ctx);
    if (await isVisibleNow(target)) return target;
    if (Date.now() > deadline) throw new Error(`Timed out after ${timeoutMs} ms waiting for ${JSON.stringify(spec)}`);
    await ctx.page.waitForTimeout(POLL_MS);
  }
}
