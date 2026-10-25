// Reading the NWEA Reports Queue table (reports.mapnwea.org/queue).

export interface QueueRow {
  readonly index: number;
  readonly id: number;
  readonly ready: boolean;
  readonly text: string;
}

const REPORT_ID = /\b(\d{6,})\b/;
const READY = /ready to view/i;

/** Parse the text of each queue row. Rows without a report id are ignored. */
export function parseQueueRows(texts: readonly string[]): QueueRow[] {
  return texts.flatMap((text, index) => {
    const m = text.match(REPORT_ID);
    return m ? [{ index, id: Number(m[1]), ready: READY.test(text), text }] : [];
  });
}

/**
 * The newest row (highest report id) that is not in `seen`, or undefined.
 * `seen` holds ids that were already in the queue or already downloaded, so a
 * stale "Ready to View" row is never mistaken for the report just requested.
 */
export function newestRow(rows: readonly QueueRow[], seen: ReadonlySet<number> = new Set()): QueueRow | undefined {
  return rows
    .filter((r) => !seen.has(r.id))
    .reduce<QueueRow | undefined>((best, r) => (!best || r.id > best.id ? r : best), undefined);
}
