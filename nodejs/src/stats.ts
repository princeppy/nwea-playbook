// Counts for the end-of-run summary: downloaded, skipped (by reason) and failed.

export interface Failure {
  readonly item: string;
  readonly error: string;
}

export interface Stats {
  readonly downloaded: number;
  readonly skipped: Readonly<Record<string, number>>;
  readonly failures: readonly Failure[];
}

export const emptyStats = (): Stats => ({ downloaded: 0, skipped: {}, failures: [] });

export const addDownload = (s: Stats): Stats => ({ ...s, downloaded: s.downloaded + 1 });

export const addSkip = (s: Stats, reason: string): Stats => ({
  ...s,
  skipped: { ...s.skipped, [reason]: (s.skipped[reason] ?? 0) + 1 },
});

export const addFailure = (s: Stats, failure: Failure): Stats => ({ ...s, failures: [...s.failures, failure] });

/** Holds the current Stats; each record call swaps in a new immutable value. */
export class StatsRecorder {
  private current: Stats = emptyStats();
  get value(): Stats {
    return this.current;
  }
  download(): void {
    this.current = addDownload(this.current);
  }
  skip(reason: string): void {
    this.current = addSkip(this.current, reason);
  }
  fail(item: string, error: string): void {
    this.current = addFailure(this.current, { item, error });
  }
}

export function formatSummary(s: Stats): string[] {
  const skips = Object.entries(s.skipped).map(([reason, n]) => `  skipped (${reason}): ${n}`);
  const failures = s.failures.map((f) => `    - ${f.item}: ${f.error}`);
  return [
    'Summary',
    `  downloaded: ${s.downloaded}`,
    ...skips,
    `  failed: ${s.failures.length}`,
    ...failures,
  ];
}
