// Save a download safely: write to "<file>.part", then rename. An interrupted run
// never leaves a half-written CSV that skipIfExists would later trust.

import { mkdirSync, renameSync, rmSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import type { Download } from 'playwright';

export async function saveDownload(dl: Download, file: string): Promise<string> {
  const out = resolve(file);
  const part = `${out}.part`;
  mkdirSync(dirname(out), { recursive: true });
  try {
    await dl.saveAs(part);
    renameSync(part, out);
  } catch (e: unknown) {
    rmSync(part, { force: true });
    throw e;
  }
  return out;
}
