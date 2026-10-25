// NWEA-specific steps:
//   checkTerm           validate the input term before anything opens
//   nweaQueueClear      empty the Reports Queue before any report is requested
//   nweaQueueDownload   wait for the new report, download it, then remove it from the queue

import type { Locator } from 'playwright';
import { dismissPopups, log, settle, sub, type Ctx, type StepHandler } from '../context.ts';
import { newestRow, parseQueueRows, type QueueRow } from '../queue.ts';
import { parseSeasons, parseTerm } from '../term.ts';
import { removeSelected, selectedCount, tick } from './queue-ui.ts';
import { saveDownload } from './save.ts';

const QUEUE_TIMEOUT_MS = 15 * 60_000;
const QUEUE_POLL_MS = 15_000;
const DOWNLOAD_TIMEOUT_MS = 120_000;

/** Queue data rows, optionally only those of one report type. */
const queueRows = (ctx: Ctx, report?: string): Locator =>
  report ? ctx.page.getByRole('row').filter({ hasText: report }) : ctx.page.getByRole('row');

/**
 * The row for one report id, found fresh each time (the table may re-render).
 * hasText sees cell texts run together ("...Growth183651331Ready..."), so \b would not
 * match; digit lookarounds do.
 */
const rowById = (ctx: Ctx, id: number): Locator =>
  ctx.page.getByRole('row').filter({ hasText: new RegExp(`(?<!\\d)${id}(?!\\d)`) }).first();

const readRows = async (ctx: Ctx, report?: string): Promise<QueueRow[]> =>
  parseQueueRows(await queueRows(ctx, report).allInnerTexts());

/** Reload the queue until a new `report` row (not in `seen`) says Ready to View. */
async function waitForReadyRow(ctx: Ctx, report: string, timeoutMs: number, pollMs: number): Promise<QueueRow> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    await settle(ctx);
    await dismissPopups(ctx);
    const row = newestRow(await readRows(ctx, report), ctx.rt.seenReports);
    if (row?.ready) return row;
    if (Date.now() > deadline) {
      throw new Error(`"${report}" was not ready in the Reports Queue after ${Math.round(timeoutMs / 60_000)} min.`);
    }
    log(ctx, row ? `report ${row.id} not ready yet, checking again in ${pollMs / 1000}s` : 'waiting for the new report to appear');
    await ctx.page.waitForTimeout(pollMs);
    await ctx.page.reload({ waitUntil: 'domcontentloaded' });
  }
}

/** Tick every row (the header's select-all box first, row by row if that misses some). */
async function tickAll(ctx: Ctx, ids: readonly number[]): Promise<void> {
  const header = ctx.page.getByRole('row').first();
  await tick(ctx, header).catch(() => {});
  if ((await selectedCount(ctx.page)) === ids.length) return;
  for (const id of ids) {
    if ((await selectedCount(ctx.page)) === ids.length) return;
    await tick(ctx, rowById(ctx, id));
  }
}

export const nweaSteps: Readonly<Record<string, StepHandler>> = {
  /** checkTerm: { term: "${term}", seasons: "${testedSeasons}" } -- stop early on a bad term. */
  checkTerm: async (ctx, a) => {
    const seasons = parseSeasons(sub(ctx, a.seasons));
    const term = parseTerm(sub(ctx, a.term), seasons);
    log(ctx, `term OK: ${term.label} (tested seasons: ${seasons.join(', ')})`);
  },

  /**
   * nweaQueueClear: {} -- run on the Reports Queue page. Removes every report in the queue,
   * so no old report can be mistaken for a new one. Any that will not go are logged and ignored.
   */
  nweaQueueClear: async (ctx) => {
    await settle(ctx);
    await dismissPopups(ctx);
    const ids = (await readRows(ctx)).map((r) => r.id);
    if (ids.length === 0) {
      log(ctx, 'Reports Queue is already empty');
      return;
    }
    log(ctx, `removing ${ids.length} report(s) from the Reports Queue: ${ids.join(', ')}`);
    await tickAll(ctx, ids);
    const left = await removeSelected(ctx, ids, async () => (await readRows(ctx)).map((r) => r.id));
    left.forEach((id) => ctx.rt.seenReports.add(id));
    if (left.length) log(ctx, `! could not remove ${left.join(', ')}; they will be ignored`);
    else log(ctx, 'Reports Queue is empty');
  },

  /** nweaQueueDownload: { report: Achievement Status and Growth, saveAs: "...", timeout, poll } */
  nweaQueueDownload: async (ctx, a) => {
    const report = sub(ctx, a.report);
    const out = sub(ctx, a.saveAs, true);
    const { id } = await waitForReadyRow(ctx, report, a.timeout ?? QUEUE_TIMEOUT_MS, a.poll ?? QUEUE_POLL_MS);
    ctx.rt.seenReports.add(id); // never pick this report again, even if removal fails
    const [dl] = await Promise.all([
      ctx.page.waitForEvent('download', { timeout: DOWNLOAD_TIMEOUT_MS }),
      rowById(ctx, id).getByText('Download', { exact: true }).first().click(),
    ]);
    const saved = await saveDownload(dl, out);
    ctx.rt.stats.download();
    log(ctx, `saved: ${saved} (report ${id}, NWEA name ${dl.suggestedFilename()})`);
    await tick(ctx, rowById(ctx, id));
    const left = await removeSelected(ctx, [id], async () => (await readRows(ctx)).map((r) => r.id));
    if (left.length) throw new Error(`File saved, but report ${id} is still in the Reports Queue. Remove it by hand.`);
    log(ctx, `removed report ${id} from the Reports Queue`);
  },
};
