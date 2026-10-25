// Playbook runner v1.5.0 (2026-10-25)
// 1.5.0  split into src/ modules with unit tests; required params (term) and secrets checked
//        at start; secrets can come from the command line (user=, pass=); "dismiss:" pop-ups;
//        radiosOf + onlySeasons loops; setAllSelects, clickIfVisible, waitForUrl, skipIfText,
//        checkTerm and nweaQueueDownload steps; waitForIdle waits for real AJAX quiet;
//        run.log and an end-of-run summary; exit code 2 when some items failed
// 1.4.0  skipIfExists step; forEach "strip" gives a clean ${name_name} for folder names
// 1.3.0  clickIfEnabled checks quickly and falls back instead of waiting 20s
// 1.2.0  goto waits for page content (domcontentloaded), 60s timeout
// 1.1.0  secrets read from .env instead of macOS Keychain
// 1.0.0  first version: YAML steps, forEach loops, downloads, secret masking

import { chromium, type Page } from 'playwright';
import { parse } from 'yaml';
import { mkdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs, USAGE } from './src/args.ts';
import { SkipSignal, type Ctx, type Runtime, type Step } from './src/context.ts';
import { runSteps } from './src/engine.ts';
import { createLogger, errorLine } from './src/logger.ts';
import { trackNetwork } from './src/network.ts';
import { resolveParams } from './src/params.ts';
import { makeMasker, normalizeSecretSpecs, resolveSecrets, withoutSecretArgs } from './src/secrets.ts';
import { formatSummary, StatsRecorder } from './src/stats.ts';

const RUNNER_VERSION = '1.5.0';
const DEFAULT_TIMEOUT_MS = 20_000;
const VIEWPORT = { width: 1440, height: 900 };

interface Setup {
  readonly pb: Record<string, unknown>;
  readonly vars: Record<string, string>;
  readonly secrets: Readonly<Record<string, string>>;
}

/** Read the playbook, inputs and secrets. Any problem throws before a browser opens. */
function setup(file: string, cli: Readonly<Record<string, string>>): Setup {
  try {
    process.loadEnvFile(resolve('.env'));
  } catch {
    // no .env file: secrets may still come from the command line or the environment
  }
  const pb = parse(readFileSync(file, 'utf8')) as Record<string, unknown>;
  if (!pb || !Array.isArray(pb.steps)) throw new Error(`${file} has no "steps:" list`);
  const specs = normalizeSecretSpecs(pb.secrets);
  const { values, missing } = resolveSecrets(specs, cli, process.env);
  if (missing.length) {
    throw new Error(`Missing secret(s): ${missing.join(', ')}. Add NAME="..." to .env in this folder.`);
  }
  const vars = {
    date: new Date().toISOString().slice(0, 10),
    ...resolveParams(pb.params, withoutSecretArgs(cli, specs), process.env),
  };
  return { pb, vars, secrets: values };
}

async function main(): Promise<number> {
  const args = parseArgs(process.argv.slice(2));
  if (!args.file) {
    process.stderr.write(USAGE + '\n');
    return 1;
  }
  let s: Setup;
  try {
    s = setup(args.file, args.vars);
  } catch (e: unknown) {
    process.stderr.write(`FAILED: ${errorLine(e)}\n`);
    return 1;
  }
  const mask = makeMasker(Object.values(s.secrets));
  const runDir = resolve('runs', new Date().toISOString().replace(/[:.]/g, '-'));
  mkdirSync(runDir, { recursive: true });
  const logger = createLogger(resolve(runDir, 'run.log'), mask);
  const stats = new StatsRecorder();

  const browser = await chromium.launch({ headless: !args.headed, slowMo: args.slowMo });
  const context = await browser.newContext({ acceptDownloads: true, viewport: VIEWPORT });
  const page = await context.newPage();
  page.setDefaultTimeout(DEFAULT_TIMEOUT_MS);

  const shot = async (p: Page, label: string): Promise<void> => {
    const file = resolve(runDir, `${label.replace(/[^\w.-]+/g, '_')}.png`);
    await p.screenshot({ path: file, fullPage: true }).catch(() => {});
    logger.info(`screenshot: ${file}`);
  };
  const rt: Runtime = {
    log: logger,
    stats,
    network: trackNetwork(page),
    dismiss: (s.pb.dismiss as Runtime['dismiss']) ?? [],
    recover: (s.pb.recover as Step[]) ?? [],
    seenReports: new Set(),
    secret: (name) => {
      const v = s.secrets[name];
      if (!v) throw new Error(`Secret "${name}" is not listed under "secrets:" in the playbook.`);
      return v;
    },
    shot,
    run: runSteps,
  };
  const ctx: Ctx = { page, vars: s.vars, pathVars: new Set(), depth: 0, rt };

  logger.info(`Runner v${RUNNER_VERSION}`);
  logger.info(`Playbook: ${String(s.pb.name ?? args.file)}`);
  logger.info(`Log: ${logger.file}`);
  let fatal = false;
  try {
    await runSteps(ctx, s.pb.steps as Step[]);
    logger.info('Finished');
  } catch (e: unknown) {
    if (e instanceof SkipSignal) logger.info(`Stopped early: ${e.reason}`);
    else {
      fatal = true;
      logger.error('FAILED: ' + errorLine(e));
      if (page.url() !== 'about:blank') await shot(page, 'fatal');
    }
  } finally {
    formatSummary(stats.value).forEach((line) => logger.info(line));
    if (args.keep) {
      logger.info('Browser left open. Press Ctrl+C to exit.');
      await new Promise(() => {});
    }
    await browser.close();
  }
  if (fatal) return 1;
  return stats.value.failures.length ? 2 : 0;
}

main().then((code) => process.exit(code));
