import { test } from 'node:test';
import assert from 'node:assert/strict';
import { addDownload, addFailure, addSkip, emptyStats, formatSummary, StatsRecorder } from '../src/stats.ts';

test('stats helpers return new objects and never mutate', () => {
  const a = emptyStats();
  const b = addSkip(addDownload(a), 'no scored students');
  assert.deepEqual(a, { downloaded: 0, skipped: {}, failures: [] });
  assert.deepEqual(b, { downloaded: 1, skipped: { 'no scored students': 1 }, failures: [] });
  assert.equal(addFailure(b, { item: 'x', error: 'y' }).failures.length, 1);
  assert.equal(b.failures.length, 0);
});

test('StatsRecorder and summary lines', () => {
  const r = new StatsRecorder();
  r.download();
  r.skip('already downloaded');
  r.skip('already downloaded');
  r.fail('course "Algebra 1"', 'Timed out');
  assert.deepEqual(formatSummary(r.value), [
    'Summary',
    '  downloaded: 1',
    '  skipped (already downloaded): 2',
    '  failed: 1',
    '    - course "Algebra 1": Timed out',
  ]);
});
