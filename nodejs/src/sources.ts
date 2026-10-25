// Where a forEach loop gets its items from:
//   optionsOf: <combobox locator>   the options of a dropdown (opened each time)
//   radiosOf:  <radio locator>      a group of radio buttons, named by their <label for=...>

import { locate, type Ctx, type StepArg } from './context.ts';
import { openOptions } from './locators.ts';
import { normalize } from './text.ts';

export interface OpenedSource {
  /** Item names, in page order. */
  readonly names: readonly string[];
  /** Select the item at `index` (an index into `names`). */
  pick(index: number): Promise<void>;
  /** Leave without picking (e.g. close the dropdown). */
  abandon(): Promise<void>;
}

async function openDropdown(ctx: Ctx, box: StepArg): Promise<OpenedSource> {
  const opts = await openOptions(ctx.page, box, ctx.vars);
  const names = (await opts.allInnerTexts()).map(normalize);
  return {
    names,
    pick: (i) => opts.nth(i).click(),
    abandon: () => ctx.page.keyboard.press('Escape'),
  };
}

async function openRadios(ctx: Ctx, spec: StepArg): Promise<OpenedSource> {
  const radios = locate(ctx, spec);
  await radios.first().waitFor({ state: 'attached' });
  const count = await radios.count();
  const names: string[] = [];
  for (let i = 0; i < count; i++) {
    const r = radios.nth(i);
    const id = await r.getAttribute('id');
    const label = id ? ctx.page.locator(`label[for="${id.replace(/"/g, '\\"')}"]`).first() : undefined;
    const text = label && (await label.count()) > 0 ? await label.innerText() : (await r.getAttribute('value')) ?? '';
    names.push(normalize(text));
  }
  return { names, pick: (i) => radios.nth(i).click(), abandon: async () => {} };
}

export function openSource(ctx: Ctx, f: StepArg): Promise<OpenedSource> {
  if (f.optionsOf) return openDropdown(ctx, f.optionsOf);
  if (f.radiosOf) return openRadios(ctx, f.radiosOf);
  throw new Error(`forEach "${f.as}" needs optionsOf (dropdown) or radiosOf (radio buttons)`);
}
