import { test } from 'node:test';
import assert from 'node:assert/strict';
import { newestRow, parseQueueRows } from '../src/queue.ts';

test('parseQueueRows reads report id and status, ignoring rows without an id', () => {
  const rows = parseQueueRows([
    'DATE CREATED REPORT TYPE DETAILS REPORT ID STATUS ACTION',
    '10/25/26 Achievement Status and Growth CSV 865.5 KB 183651328 Ready to View Deleting on 11/08/26 Download',
    '10/25/26 Achievement Status and Growth CSV 183651330 Processing',
  ]);
  assert.deepEqual(rows.map((r) => [r.index, r.id, r.ready]), [[1, 183651328, true], [2, 183651330, false]]);
});

test('newestRow picks the highest report id', () => {
  const rows = parseQueueRows(['a 183651331 Ready to View', 'b 183651334 Processing', 'c 183651330 Ready to View']);
  assert.equal(newestRow(rows)?.id, 183651334);
  assert.equal(newestRow([]), undefined);
});

test('newestRow ignores reports that were already in the queue', () => {
  const rows = parseQueueRows(['a 183651331 Ready to View', 'b 183651334 Ready to View']);
  assert.equal(newestRow(rows, new Set([183651334]))?.id, 183651331);
  assert.equal(newestRow(rows, new Set([183651331, 183651334])), undefined);
});
