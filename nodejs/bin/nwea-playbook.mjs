#!/usr/bin/env node
// Run from any folder, no install: npx <repo>/nodejs ["Spring 2025-2026"] [--headed] [--keep] [--slow=300]
// npx links to this checkout, so edits apply at once and the repo's nwea-class-profile.yaml
// is the default playbook.
// Runs in the current folder: .env is read from it, downloads/ and runs/ are written to it.
// Installs Playwright's Chromium on first use, then hands over to run.ts (TypeScript, via tsx).

import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const MIN_NODE = 22;
if (Number(process.versions.node.split('.')[0]) < MIN_NODE) {
  process.stderr.write(`Node.js ${MIN_NODE} or newer is needed (found ${process.version}).\n`);
  process.exit(1);
}

const nodejsDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const bundledPlaybook = resolve(nodejsDir, '..', 'nwea-class-profile.yaml');

function ensureChromium(executable) {
  if (existsSync(executable)) return;
  process.stderr.write("First run: installing Playwright's Chromium...\n");
  const cli = resolve(dirname(createRequire(import.meta.url).resolve('playwright/package.json')), 'cli.js');
  const { status } = spawnSync(process.execPath, [cli, 'install', 'chromium'], { stdio: 'inherit' });
  if (status !== 0) process.exit(1);
}

// Node's own type stripping refuses files under node_modules, so tsx loads the .ts files
(await import('tsx/esm/api')).register();
const { chromium } = await import('playwright');
ensureChromium(chromium.executablePath());

const { launchArgs } = await import('../src/launch.ts');
// run.ts reads its arguments from process.argv
process.argv = [process.argv[0], resolve(nodejsDir, 'run.ts'), ...launchArgs(process.argv.slice(2), bundledPlaybook)];
await import('../run.ts');
