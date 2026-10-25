// Playbook runner v1.4.0 (2026-10-17)
// 1.4.0  skipIfExists step; forEach "strip" gives a clean ${name_name} for folder names
// 1.3.0  clickIfEnabled checks quickly and falls back instead of waiting 20s
// 1.2.0  goto waits for page content (domcontentloaded), 60s timeout
// 1.1.0  secrets read from .env instead of macOS Keychain
// 1.0.0  first version: YAML steps, forEach loops, downloads, secret masking

import { chromium, type Page, type Locator } from 'playwright';
import { parse } from 'yaml';
import { readFileSync, mkdirSync, existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

const RUNNER_VERSION = '1.4.0';
const SKIP = new Error('skip');

type Step = Record<string, any>;

const argv = process.argv.slice(2);
const file = argv.find((a) => /\.ya?ml$/i.test(a));
if (!file) {
  console.error('Usage: npx tsx run.ts <playbook.yaml> [--headed] [--keep] [--slow=300] [name=value ...]');
  process.exit(1);
}
const headed = argv.includes('--headed') || argv.includes('--keep');
const slowArg = argv.find((a) => a.startsWith('--slow='));
const slowMo = slowArg ? Number(slowArg.split('=')[1]) : headed ? 200 : 0;

const pb = parse(readFileSync(file, 'utf8'));

const vars: Record<string, string> = { date: new Date().toISOString().slice(0, 10) };
for (const [k, v] of Object.entries(pb.params ?? {})) vars[k] = String(v);
for (const a of argv) {
  const m = a.match(/^(\w+)=(.*)$/);
  if (m) vars[m[1]] = m[2];
}
const loopVars = new Set<string>();
const secretValues: string[] = [];
const runDir = resolve('runs', new Date().toISOString().replace(/[:.]/g, '-'));
mkdirSync(runDir, { recursive: true });

try {
  process.loadEnvFile(resolve('.env'));
} catch {}

function secret(name: string): string {
  const v = process.env[name];
  if (!v) throw new Error(`Secret "${name}" not found. Add a line ${name}="..." to the .env file in this folder.`);
  secretValues.push(v);
  return v;
}

const mask = (s: string) => secretValues.reduce((t, v) => (v ? t.split(v).join('***') : t), s);
const log = (s: string, depth = 0) => console.log('  '.repeat(depth) + mask(s));

function sub(text: unknown, forPath = false): string {
  return String(text).replace(/\$\{(\w+)\}/g, (_m, k: string) => {
    if (!(k in vars)) throw new Error(`Unknown variable \${${k}}`);
    const v = vars[k];
    return forPath && loopVars.has(k) ? v.replace(/[\\/:*?"<>|]+/g, '-').replace(/,/g, '').trim() : v;
  });
}

const xq = (s: string) => (s.includes('"') ? `'${s}'` : `"${s}"`);

function find(page: Page, spec: any): Locator {
  if (typeof spec === 'string') spec = { text: spec };
  let l: Locator;
  if (spec.css) l = page.locator(sub(spec.css));
  else if (spec.testid) l = page.getByTestId(sub(spec.testid));
  else if (spec.field)
    l = page.locator(`xpath=(//*[normalize-space(.)=${xq(sub(spec.field))}]/following::*[@role="combobox"])[1]`);
  else if (spec.role)
    l = page.getByRole(spec.role as any, spec.name ? { name: sub(spec.name), exact: !!spec.exact } : undefined);
  else if (spec.text) l = page.getByText(sub(spec.text), { exact: !!spec.exact });
  else throw new Error(`Unknown locator ${JSON.stringify(spec)}`);
  return spec.nth !== undefined ? l.nth(spec.nth) : l;
}

async function openOptions(page: Page, box: any): Promise<Locator> {
  await find(page, box).click();
  const opts = page.getByRole('option');
  await opts.first().waitFor({ state: 'visible', timeout: 15000 });
  return opts;
}

async function choose(page: Page, box: any, value: string) {
  await openOptions(page, box);
  let target = page.getByRole('option', { name: value, exact: true });
  if ((await target.count()) === 0) {
    await find(page, box).fill(value);
    target = page.getByRole('option', { name: value, exact: true });
  }
  await target.first().click();
}

async function shot(page: Page, label: string) {
  const p = resolve(runDir, `${label.replace(/[^\w.-]+/g, '_')}.png`);
  await page.screenshot({ path: p, fullPage: true }).catch(() => {});
  log(`screenshot: ${p}`);
}

async function forEach(page: Page, f: any, depth: number) {
  const skip = new Set<string>((f.skip ?? []).map(String));
  const limit = f.limit ?? Infinity;
  for (let i = 0; i < limit; i++) {
    await run(page, f.before ?? [], depth + 1);
    const opts = await openOptions(page, f.optionsOf);
    const names = (await opts.allInnerTexts()).map((t) => t.replace(/\s+/g, ' ').trim());
    const list = names.map((n, idx) => ({ n, idx })).filter((o) => o.n && !skip.has(o.n));
    if (i >= list.length) {
      await page.keyboard.press('Escape');
      await run(page, f.close ?? [], depth + 1);
      break;
    }
    const pick = list[i];
    await opts.nth(pick.idx).click();
    vars[f.as] = pick.n;
    let clean = pick.n;
    for (const s of f.strip ?? []) clean = clean.split(String(s)).join('').trim();
    vars[`${f.as}_name`] = clean;
    loopVars.add(f.as);
    loopVars.add(`${f.as}_name`);
    log(`> ${f.as} = ${pick.n}  (${i + 1} of ${list.length})`, depth);
    try {
      await run(page, f.do ?? [], depth + 1);
    } catch (e: any) {
      if (e === SKIP) continue;
      if (f.onError !== 'continue') throw e;
      log(`x ${f.as} "${pick.n}" failed: ${String(e?.message ?? e).split('\n')[0]}`, depth);
      await shot(page, `${f.as}-${pick.n}`);
      await run(page, pb.recover ?? [], depth + 1);
    }
  }
}

async function run(page: Page, steps: Step[], depth = 0) {
  for (const step of steps) {
    const type = Object.keys(step)[0];
    const a = step[type];
    if (type !== 'forEach') log(`${type} ${a && typeof a === 'object' ? JSON.stringify(a) : a ?? ''}`, depth);
    switch (type) {
      case 'goto':
        await page.goto(sub(typeof a === 'object' ? a.url : a), {
          waitUntil: 'domcontentloaded',
          timeout: (typeof a === 'object' && a.timeout) || 60000,
        });
        break;
      case 'click':
        await find(page, a).click();
        break;
      case 'fill':
        await find(page, a).fill(a.secret ? secret(a.secret) : sub(a.value));
        break;
      case 'waitFor':
        await find(page, a).first().waitFor({ state: 'visible', timeout: a.timeout ?? 30000 });
        break;
      case 'waitForIdle':
        await page.waitForLoadState('networkidle', { timeout: a ?? 15000 }).catch(() => {});
        break;
      case 'choose':
        await choose(page, a.in, sub(a.option));
        break;
      case 'clickIfEnabled': {
        const t = find(page, a.target).first();
        const enabled =
          (await t.count()) > 0 && (await t.isEnabled({ timeout: 3000 }).catch(() => false));
        if (enabled) await t.click();
        else if (a.otherwise) await find(page, a.otherwise).click();
        break;
      }
      case 'download': {
        const out = resolve(sub(a.saveAs, true));
        mkdirSync(dirname(out), { recursive: true });
        const [dl] = await Promise.all([
          page.waitForEvent('download', { timeout: a.timeout ?? 60000 }),
          find(page, a.click).click(),
        ]);
        await dl.saveAs(out);
        log(`saved: ${out}`, depth);
        break;
      }
      case 'skipIfExists': {
        const p = resolve(sub(a.file, true));
        if (existsSync(p)) {
          log(`skip, already downloaded: ${p}`, depth);
          await run(page, a.cleanup ?? [], depth);
          throw SKIP;
        }
        break;
      }
      case 'screenshot':
        await shot(page, sub(a));
        break;
      case 'sleep':
        await page.waitForTimeout(Number(a));
        break;
      case 'forEach':
        await forEach(page, a, depth);
        break;
      default:
        throw new Error(`Unknown step "${type}"`);
    }
  }
}

async function main() {
  const browser = await chromium.launch({ headless: !headed, slowMo });
  const context = await browser.newContext({ acceptDownloads: true, viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();
  page.setDefaultTimeout(20000);
  let ok = true;
  console.log(`Runner v${RUNNER_VERSION}`);
  console.log(`Playbook: ${pb.name}`);
  try {
    await run(page, pb.steps ?? []);
    console.log('Finished');
  } catch (e: any) {
    ok = false;
    console.error('FAILED: ' + mask(String(e?.message ?? e)));
    await shot(page, 'fatal');
  } finally {
    if (argv.includes('--keep')) {
      console.log('Browser left open. Press Ctrl+C to exit.');
      await new Promise(() => {});
    }
    await browser.close();
  }
  process.exit(ok ? 0 : 1);
}

main();