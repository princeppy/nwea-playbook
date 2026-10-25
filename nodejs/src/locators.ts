// Turns a YAML locator into a Playwright Locator, and picks options from dropdowns.
//
//   "Some text"                      text (substring)
//   { css: ... } { testid: ... }     CSS selector / data-testid
//   { role: button, name: Update }   ARIA role (+ name, exact)
//   { text: ..., exact: true }       text
//   { regex: '\d+ out of \d+' }      text matching a regular expression
//   { endsWith: '| ${course}' }      text ending with exactly this (special characters are literal)
//   { field: Instructor }            first combobox after a label with that text
//   { any: [ ..., ... ] }            the first of several alternatives
//   nth: 2                           pick one of several matches

import type { Locator, Page } from 'playwright';
import { matchOptionIndex, normalize } from './text.ts';
import { substitute, type Vars } from './vars.ts';

export type LocatorSpec = string | Readonly<Record<string, unknown>>;

const OPTIONS_TIMEOUT_MS = 15_000;
const FILTER_SETTLE_MS = 500;

/** Quote a string for XPath. */
const xq = (s: string): string => (s.includes('"') ? `'${s}'` : `"${s}"`);

const escapeRegex = (s: string): string => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

type AriaRole = Parameters<Page['getByRole']>[0];

export function find(page: Page, spec: LocatorSpec, vars: Vars): Locator {
  const s: Readonly<Record<string, unknown>> = typeof spec === 'string' ? { text: spec } : spec;
  const t = (v: unknown): string => substitute(v, vars);
  let l: Locator;
  if (Array.isArray(s.any)) {
    l = (s.any as LocatorSpec[]).map((a) => find(page, a, vars)).reduce((a, b) => a.or(b)).first();
  } else if (s.css) l = page.locator(t(s.css));
  else if (s.testid) l = page.getByTestId(t(s.testid));
  else if (s.field)
    l = page.locator(`xpath=(//*[normalize-space(.)=${xq(t(s.field))}]/following::*[@role="combobox"])[1]`);
  else if (s.role)
    l = page.getByRole(s.role as AriaRole, s.name ? { name: t(s.name), exact: !!s.exact } : undefined);
  else if (s.regex) l = page.getByText(new RegExp(t(s.regex)));
  else if (s.endsWith) l = page.getByText(new RegExp(`${escapeRegex(t(s.endsWith))}\\s*$`));
  else if (s.text) l = page.getByText(t(s.text), { exact: !!s.exact });
  else throw new Error(`Unknown locator ${JSON.stringify(spec)}`);
  return s.nth !== undefined ? l.nth(Number(s.nth)) : l;
}

/** Click a combobox and wait for its options to show. */
export async function openOptions(page: Page, box: LocatorSpec, vars: Vars): Promise<Locator> {
  await find(page, box, vars).click();
  const opts = page.getByRole('option');
  await opts.first().waitFor({ state: 'visible', timeout: OPTIONS_TIMEOUT_MS });
  return opts;
}

const optionNames = async (opts: Locator): Promise<string[]> => (await opts.allInnerTexts()).map(normalize);

/**
 * Pick `value` in a combobox. Accepts an exact match or the value plus a bracketed
 * note ("Fall 2026-2027 (Most Recent)"). Types the value to filter long lists.
 */
export async function choose(page: Page, box: LocatorSpec, value: string, vars: Vars): Promise<void> {
  const opts = await openOptions(page, box, vars);
  let names = await optionNames(opts);
  let idx = matchOptionIndex(names, value);
  if (idx < 0) {
    await find(page, box, vars).fill(value);
    await page.waitForTimeout(FILTER_SETTLE_MS);
    names = await optionNames(page.getByRole('option'));
    idx = matchOptionIndex(names, value);
  }
  if (idx < 0) throw new Error(`"${value}" is not in the list. Options: ${names.slice(0, 20).join(' | ')}`);
  await page.getByRole('option').nth(idx).click();
}
