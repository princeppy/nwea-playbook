// Clicking around the NWEA Reports Queue: ticking rows and removing them.
//
// The queue's checkboxes are custom-drawn: the real <input> is hidden, so a plain
// check() times out (seen on the live site, 25 Oct 2026). We try check() briefly,
// then click the box drawn in the row's first cell, and confirm with the page's
// "N Reports Selected" counter.

import type { Dialog, Locator, Page } from 'playwright';
import { dismissPopups, settle, type Ctx } from '../context.ts';

const CHECK_TIMEOUT_MS = 3_000;
const CONFIRM_WAIT_MS = 3_000;
const REMOVE_TIMEOUT_MS = 30_000;
const CONFIRM_BUTTON = /^(Remove|Yes|OK|Confirm)$/i;
const SELECTED_COUNT = /(\d+)\s+Reports?\s+Selected/i;

/** The number in "N Reports Selected", or -1 if the counter is not on the page. */
export async function selectedCount(page: Page): Promise<number> {
  const text = await page.getByText(SELECTED_COUNT).first().innerText().catch(() => '');
  const m = text.match(SELECTED_COUNT);
  return m ? Number(m[1]) : -1;
}

/** Tick the checkbox in `row`. Throws if the selection counter does not go up. */
export async function tick(ctx: Ctx, row: Locator): Promise<void> {
  await dismissPopups(ctx);
  const before = await selectedCount(ctx.page);
  const box = row.getByRole('checkbox').first();
  const ticked = async (): Promise<boolean> =>
    before < 0 ? box.isChecked().catch(() => false) : (await selectedCount(ctx.page)) > before;

  await box.check({ timeout: CHECK_TIMEOUT_MS }).catch(() => {});
  if (await ticked()) return;
  await row.locator('td, th').first().click({ timeout: CHECK_TIMEOUT_MS }).catch(() => {});
  await ctx.page.waitForTimeout(300);
  if (await ticked()) return;
  await box.check({ force: true, timeout: CHECK_TIMEOUT_MS }).catch(() => {});
  if (await ticked()) return;
  throw new Error('Could not tick the Reports Queue checkbox');
}

/**
 * Click Remove Selected (accepting a browser or on-page confirmation), then wait until
 * none of `ids` is listed. Returns the ids still listed after the wait.
 */
export async function removeSelected(
  ctx: Ctx,
  ids: readonly number[],
  listIds: () => Promise<number[]>,
): Promise<number[]> {
  const { page } = ctx;
  const accept = (d: Dialog): void => {
    d.accept().catch(() => {});
  };
  page.on('dialog', accept);
  try {
    await page.getByText('Remove Selected', { exact: true }).first().click();
    const confirm = page.getByRole('dialog').getByRole('button', { name: CONFIRM_BUTTON }).first();
    if (await confirm.waitFor({ state: 'visible', timeout: CONFIRM_WAIT_MS }).then(() => true, () => false)) {
      await confirm.click();
    }
  } finally {
    page.off('dialog', accept);
  }
  const deadline = Date.now() + REMOVE_TIMEOUT_MS;
  let left: number[] = [...ids];
  while (Date.now() < deadline) {
    await settle(ctx);
    const listed = new Set(await listIds());
    left = ids.filter((id) => listed.has(id));
    if (left.length === 0) return [];
    await page.waitForTimeout(1_000);
  }
  return left;
}
