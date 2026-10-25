// Waits until the page has had no page/XHR/fetch request in flight for a moment.
// page.waitForLoadState('networkidle') returns at once after the first load, so it
// cannot see the AJAX refreshes NWEA does after a dropdown or radio change.

import type { Page, Request } from 'playwright';

const TRACKED_TYPES = new Set(['document', 'xhr', 'fetch']);
const POLL_MS = 100;
const STALE_MS = 30_000; // ignore requests that never finish (long-polling, downloads)
const DEFAULT_QUIET_MS = 500;

export interface NetworkTracker {
  /** Resolves true once quiet for `quietMs`, or false when `timeoutMs` passes first. */
  waitQuiet(timeoutMs: number, quietMs?: number): Promise<boolean>;
}

export function trackNetwork(page: Page): NetworkTracker {
  const inFlight = new Map<Request, number>();
  page.on('request', (r) => {
    if (TRACKED_TYPES.has(r.resourceType())) inFlight.set(r, Date.now());
  });
  const done = (r: Request): void => {
    inFlight.delete(r);
  };
  page.on('requestfinished', done);
  page.on('requestfailed', done);

  const busy = (): boolean => {
    const now = Date.now();
    return [...inFlight.values()].some((started) => now - started < STALE_MS);
  };

  return {
    async waitQuiet(timeoutMs, quietMs = DEFAULT_QUIET_MS) {
      const deadline = Date.now() + timeoutMs;
      let quietSince = busy() ? 0 : Date.now();
      while (Date.now() < deadline) {
        if (busy()) quietSince = 0;
        else if (!quietSince) quietSince = Date.now();
        else if (Date.now() - quietSince >= quietMs) return true;
        await page.waitForTimeout(POLL_MS);
      }
      return false;
    },
  };
}
