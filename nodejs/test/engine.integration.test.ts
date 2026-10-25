// Runs the engine against small local pages in a real (headless) Chromium.
// Pages are served from memory with page.route, so nothing touches the network.

import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { chromium, type Browser, type Page } from 'playwright';
import type { Ctx, Runtime, Step } from '../src/context.ts';
import { runSteps } from '../src/engine.ts';
import { trackNetwork } from '../src/network.ts';
import { StatsRecorder } from '../src/stats.ts';
import { ASG_PAGE, CLASS_PAGE, COMBO_PAGE, QUEUE_PAGE } from './fixtures/pages.ts';

let browser: Browser;
let dir: string;

before(async () => {
  browser = await chromium.launch();
  dir = mkdtempSync(join(tmpdir(), 'playbook-test-'));
});
after(async () => {
  await browser.close();
  rmSync(dir, { recursive: true, force: true });
});

async function setup(vars: Record<string, string> = {}): Promise<{ ctx: Ctx; page: Page; stats: StatsRecorder; lines: string[] }> {
  const page = await (await browser.newContext({ acceptDownloads: true })).newPage();
  page.setDefaultTimeout(5_000);
  const pages: Record<string, string> = { '/asg': ASG_PAGE, '/queue': QUEUE_PAGE, '/class': CLASS_PAGE, '/combo': COMBO_PAGE };
  await page.route('http://test.local/**', (route) => {
    const body = pages[new URL(route.request().url()).pathname];
    return body ? route.fulfill({ contentType: 'text/html', body }) : route.fulfill({ status: 404 });
  });
  const stats = new StatsRecorder();
  const lines: string[] = [];
  const rt: Runtime = {
    log: { file: '', info: (m) => lines.push(m), error: (m) => lines.push(m) },
    stats,
    network: trackNetwork(page),
    dismiss: [{ css: '#alert-ok' }],
    recover: [],
    seenReports: new Set(),
    secret: () => 'x',
    shot: async () => {},
    run: runSteps,
  };
  const ctx: Ctx = { page, vars: { dir, ...vars }, pathVars: new Set(), depth: 0, rt };
  return { ctx, page, stats, lines };
}

test('ASG loop: sets both term dropdowns, skips untested seasons, downloads from the queue', async () => {
  const { ctx, page, stats, lines } = await setup({ term: 'Spring 2025-2026', testedSeasons: 'Fall, Spring' });
  // Two stale reports from earlier runs sit in the queue; they are cleared before any request.
  await page.goto('http://test.local/queue');
  await page.evaluate(() =>
    sessionStorage.setItem('reports', JSON.stringify([{ id: 183000001, readyAt: 0 }, { id: 183000002, readyAt: 0 }])),
  );
  const steps: Step[] = [
    { checkTerm: { term: '${term}', seasons: '${testedSeasons}' } },
    { goto: 'http://test.local/queue' },
    { nweaQueueClear: {} },
    {
      forEach: {
        as: 'period',
        radiosOf: { css: 'input[type="radio"][name$=":growthComparisonPeriod"]' },
        onlySeasons: '${testedSeasons}',
        before: [
          { goto: 'http://test.local/asg' },
          { setAllSelects: { optionMatches: '^(Fall|Winter|Spring|Summer) \\d{4}-\\d{4}', option: '${term}', min: 2 } },
        ],
        do: [
          { skipIfExists: { file: '${dir}/ASG - ${period}.csv' } },
          { click: { any: [{ role: 'button', name: 'Create Spreadsheet' }, { text: 'Create Spreadsheet', exact: true }] } },
          { waitForUrl: { url: '**/queue**', timeout: 5000 } },
          { nweaQueueDownload: { report: 'Achievement Status and Growth', saveAs: '${dir}/ASG - ${period}.csv', poll: 200, timeout: 5000 } },
        ],
      },
    },
  ];
  await runSteps(ctx, steps);

  const kept = [
    'Spring 2026 - Spring 2027 Growth Projection Report',
    'Fall 2025 - Spring 2026 Growth Summary Report',
    'Spring 2025 - Spring 2026 Growth Summary Report',
  ];
  kept.forEach((p) => assert.ok(existsSync(join(dir, `ASG - ${p}.csv`)), p));
  assert.ok(!existsSync(join(dir, 'ASG - Winter 2026 - Spring 2026 Growth Summary Report.csv')));
  assert.equal(readFileSync(join(dir, `ASG - ${kept[0]}.csv`), 'utf8'), 'id,score\n1,200\n');
  assert.equal(stats.value.downloaded, 3);
  assert.ok(lines.some((l) => l.includes('dismissed pop-up')), 'System Alert was dismissed');
  assert.ok(lines.some((l) => l.includes('set dropdown') && l.includes('Spring 2025-2026')));
  assert.ok(lines.some((l) => l.includes('removed report')));
  assert.ok(lines.some((l) => l.includes('removing 2 report(s) from the Reports Queue: 183000001, 183000002')));
  assert.ok(lines.some((l) => l.includes('Reports Queue is empty')));
  assert.ok(!lines.some((l) => l.includes('report 18300000')), 'stale reports were not downloaded');

  // A second run skips everything that already exists.
  const again = await setup({ term: 'Spring 2025-2026', testedSeasons: 'Fall, Spring' });
  await runSteps(again.ctx, steps);
  assert.deepEqual(again.stats.value.skipped, { 'already downloaded': 3 });
});

test('checkTerm stops a run with a season the school does not test', async () => {
  const { ctx } = await setup({ term: 'Winter 2026-2027', testedSeasons: 'Fall, Spring' });
  await assert.rejects(runSteps(ctx, [{ checkTerm: { term: '${term}', seasons: '${testedSeasons}' } }]), /only tests in Fall and Spring/);
});

test('skipIfText skips a class with 0 scored students and downloads the others', async () => {
  const { ctx, stats } = await setup();
  const steps: Step[] = [
    {
      forEach: {
        as: 'course',
        onError: 'continue',
        radiosOf: { css: 'input[name="course"]' },
        before: [{ goto: 'http://test.local/class' }],
        do: [
          { waitForIdle: 2000 },
          { skipIfText: { target: { regex: '\\d+ out of \\d+ students? ha(s|ve) an official RIT score' }, matches: '^0 out of', reason: 'no scored students' } },
          { download: { click: { testid: 'downloadCSVButton' }, saveAs: '${dir}/CP - ${course}.csv' } },
        ],
      },
    },
  ];
  await runSteps(ctx, steps);
  assert.ok(existsSync(join(dir, 'CP - Reading.csv')));
  assert.ok(!existsSync(join(dir, 'CP - Algebra 1.csv')));
  assert.deepEqual(stats.value, { downloaded: 1, skipped: { 'no scored students': 1 }, failures: [] });
});

test('choose picks "(Most Recent)" options and filters long lists by typing', async () => {
  const { ctx, page } = await setup({ term: 'Fall 2026-2027' });
  await runSteps(ctx, [
    { goto: 'http://test.local/combo' },
    { choose: { in: { field: 'Term Tested' }, option: '${term}' } },
  ]);
  assert.equal(await page.locator('#chosen').textContent(), 'term=Fall 2026-2027 (Most Recent)');
  await runSteps(ctx, [{ choose: { in: { field: 'Instructor' }, option: 'Daly, Louise' } }]);
  assert.equal(await page.locator('#chosen').textContent(), 'teacher=Daly, Louise');
  await assert.rejects(runSteps(ctx, [{ choose: { in: { field: 'Instructor' }, option: 'Nobody' } }]), /"Nobody" is not in the list/);
});

test('forEach over a dropdown visits every option', async () => {
  const { ctx, lines } = await setup();
  await runSteps(ctx, [
    { goto: 'http://test.local/combo' },
    { forEach: { as: 'teacher', optionsOf: { field: 'Instructor' }, skip: ['AbuFayad, Hiba'], do: [] } },
  ]);
  const picked = lines.filter((l) => l.includes('> teacher ='));
  assert.deepEqual(picked.map((l) => l.trim()), ['> teacher = Abbasi, Nazneen  (1 of 2)', '> teacher = Daly, Louise  (2 of 2)']);
});

test('unknown steps fail with a helpful message', async () => {
  const { ctx } = await setup();
  await assert.rejects(runSteps(ctx, [{ clik: 'x' }]), /Unknown step "clik"/);
});
