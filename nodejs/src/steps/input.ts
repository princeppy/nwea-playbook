// Steps that act on the page: click, fill, choose, setAllSelects, clickIfEnabled, clickIfVisible.

import type { Locator } from 'playwright';
import { locate, log, settle, sub, waitVisible, type Ctx, type StepHandler } from '../context.ts';
import { choose } from '../locators.ts';
import { matchOptionIndex, normalize } from '../text.ts';

const ENABLED_CHECK_MS = 3_000;
const IF_VISIBLE_TIMEOUT_MS = 3_000;

const selectTexts = (sel: Locator): Promise<{ options: string[]; selected: string }> =>
  sel.evaluate((s) => {
    const el = s as HTMLSelectElement;
    return { options: Array.from(el.options).map((o) => o.text), selected: el.selectedOptions[0]?.text ?? '' };
  });

/**
 * setAllSelects: { optionMatches: '^(Fall|Spring) \d{4}-\d{4}', option: "${term}", min: 2 }
 * Sets every native <select> that offers an option matching `optionMatches` to `option`.
 * Used for the ASG page, where Term Rostered and Term Tested are both term dropdowns.
 */
interface SelectPass {
  readonly matched: number;
  readonly changed: number;
}

/** One pass over the page's selects; returns how many matched and how many it had to change. */
async function setSelectsOnce(ctx: Ctx, pattern: RegExp, value: string): Promise<SelectPass> {
  const selects = ctx.page.locator('select');
  let matched = 0;
  let changed = 0;
  for (let i = 0; i < (await selects.count()); i++) {
    const sel = selects.nth(i); // re-queried each time: an AJAX refresh may replace the elements
    const { options, selected } = await selectTexts(sel);
    if (!options.some((o) => pattern.test(normalize(o)))) continue;
    matched++;
    const idx = matchOptionIndex(options, value);
    if (idx < 0) throw new Error(`"${value}" is not offered. Options: ${options.map(normalize).join(' | ')}`);
    if (normalize(selected) === normalize(options[idx])) continue;
    await sel.selectOption({ index: idx });
    changed++;
    log(ctx, `set dropdown ${i + 1} to "${normalize(options[idx])}"`);
    await settle(ctx);
  }
  return { matched, changed };
}

const MAX_SELECT_PASSES = 3;

async function setAllSelects(ctx: Ctx, a: Record<string, unknown>): Promise<void> {
  const value = sub(ctx, a.option);
  const pattern = new RegExp(String(a.optionMatches));
  const min = Number(a.min ?? 1);
  // Repeat until a pass changes nothing: changing one dropdown can reset another.
  for (let pass = 1; pass <= MAX_SELECT_PASSES; pass++) {
    const { matched, changed } = await setSelectsOnce(ctx, pattern, value);
    if (matched < min) throw new Error(`Expected at least ${min} dropdown(s) matching ${pattern}, found ${matched}.`);
    if (changed === 0) return;
  }
  throw new Error(`Dropdowns kept changing; could not set them all to "${value}".`);
}

export const inputSteps: Readonly<Record<string, StepHandler>> = {
  click: async (ctx, a) => {
    await locate(ctx, a).click();
  },

  fill: async (ctx, a) => {
    await locate(ctx, a).fill(a.secret ? ctx.rt.secret(a.secret) : sub(ctx, a.value));
  },

  choose: async (ctx, a) => {
    await choose(ctx.page, a.in, sub(ctx, a.option), ctx.vars);
  },

  setAllSelects,

  clickIfEnabled: async (ctx, a) => {
    const t = locate(ctx, a.target).first();
    const enabled = (await t.count()) > 0 && (await t.isEnabled({ timeout: ENABLED_CHECK_MS }).catch(() => false));
    if (enabled) await t.click();
    else if (a.otherwise) await locate(ctx, a.otherwise).click();
  },

  /** clickIfVisible: { target: ..., timeout: 3000 } -- click only if it shows up in time. */
  clickIfVisible: async (ctx, a) => {
    const shown = await waitVisible(ctx, a.target ?? a, a.timeout ?? IF_VISIBLE_TIMEOUT_MS).catch(() => undefined);
    if (shown) await shown.click();
  },
};
