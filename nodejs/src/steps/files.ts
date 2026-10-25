// Steps about files and skipping: download, skipIfExists, skipIfText.

import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { locate, log, SkipSignal, sub, waitVisible, type StepHandler } from '../context.ts';
import { normalize } from '../text.ts';
import { saveDownload } from './save.ts';

const DOWNLOAD_TIMEOUT_MS = 60_000;
const TEXT_TIMEOUT_MS = 30_000;

export const SKIP_EXISTS = 'already downloaded';

export const fileSteps: Readonly<Record<string, StepHandler>> = {
  download: async (ctx, a) => {
    const [dl] = await Promise.all([
      ctx.page.waitForEvent('download', { timeout: a.timeout ?? DOWNLOAD_TIMEOUT_MS }),
      locate(ctx, a.click).click(),
    ]);
    const out = await saveDownload(dl, sub(ctx, a.saveAs, true));
    ctx.rt.stats.download();
    log(ctx, `saved: ${out}`);
  },

  skipIfExists: async (ctx, a) => {
    const p = resolve(sub(ctx, a.file, true));
    if (!existsSync(p)) return;
    log(ctx, `skip, already downloaded: ${p}`);
    await ctx.rt.run(ctx, a.cleanup ?? []);
    throw new SkipSignal(SKIP_EXISTS);
  },

  /**
   * skipIfText: { target: <locator>, matches: '^0 out of', reason: no scored students, cleanup: [...] }
   * Reads the target's text and skips the current loop item when it matches.
   */
  skipIfText: async (ctx, a) => {
    const el = await waitVisible(ctx, a.target, a.timeout ?? TEXT_TIMEOUT_MS);
    const text = normalize(await el.innerText());
    if (!new RegExp(sub(ctx, a.matches)).test(text)) return;
    const reason = String(a.reason ?? 'text matched');
    log(ctx, `skip, ${reason}: "${text}"`);
    await ctx.rt.run(ctx, a.cleanup ?? []);
    throw new SkipSignal(reason);
  },
};
