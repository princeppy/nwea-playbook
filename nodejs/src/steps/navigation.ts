// Steps that move around or wait: goto, waitFor, waitForIdle, waitForUrl, sleep, screenshot.

import { log, settle, sub, waitVisible, type StepHandler } from '../context.ts';

const GOTO_TIMEOUT_MS = 60_000;
const WAIT_FOR_TIMEOUT_MS = 30_000;
const IDLE_TIMEOUT_MS = 15_000;
const URL_TIMEOUT_MS = 60_000;

export const navigationSteps: Readonly<Record<string, StepHandler>> = {
  goto: async (ctx, a) => {
    const url = sub(ctx, typeof a === 'object' ? a.url : a);
    const timeout = (typeof a === 'object' && a.timeout) || GOTO_TIMEOUT_MS;
    await ctx.page.goto(url, { waitUntil: 'domcontentloaded', timeout });
  },

  /** waitFor: <locator>, with optional timeout; optional: true logs instead of failing. */
  waitFor: async (ctx, a) => {
    const timeout = (typeof a === 'object' && a.timeout) || WAIT_FOR_TIMEOUT_MS;
    try {
      await waitVisible(ctx, a, timeout);
    } catch (e: unknown) {
      if (!(typeof a === 'object' && a.optional)) throw e;
      log(ctx, `(not seen within ${timeout / 1000}s, continuing)`);
    }
  },

  /** Wait until no page/AJAX request has been in flight for half a second. */
  waitForIdle: async (ctx, a) => {
    await settle(ctx, typeof a === 'number' ? a : IDLE_TIMEOUT_MS);
  },

  /** waitForUrl: "**\/queue**"  or  { url: "**\/queue**", timeout: 300000 } */
  waitForUrl: async (ctx, a) => {
    const url = sub(ctx, typeof a === 'object' ? a.url : a);
    const timeout = (typeof a === 'object' && a.timeout) || URL_TIMEOUT_MS;
    await ctx.page.waitForURL(url, { timeout, waitUntil: 'domcontentloaded' });
  },

  sleep: async (ctx, a) => {
    await ctx.page.waitForTimeout(Number(a));
  },

  screenshot: async (ctx, a) => {
    await ctx.rt.shot(ctx.page, sub(ctx, a));
  },
};
