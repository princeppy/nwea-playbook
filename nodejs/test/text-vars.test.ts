import { test } from 'node:test';
import assert from 'node:assert/strict';
import { matchOptionIndex, normalize } from '../src/text.ts';
import { sanitizeForPath, substitute } from '../src/vars.ts';

test('normalize collapses whitespace and nbsp', () => {
  assert.equal(normalize(' Spring 2026 -\n Spring 2027  '), 'Spring 2026 - Spring 2027');
});

test('matchOptionIndex prefers an exact match', () => {
  assert.equal(matchOptionIndex(['Fall 2026-2027 (Most Recent)', 'Fall 2026-2027'], 'Fall 2026-2027'), 1);
});

test('matchOptionIndex accepts a bracketed suffix such as (Most Recent)', () => {
  const opts = ['Spring 2025-2026', 'Fall 2026-2027 (Most Recent)'];
  assert.equal(matchOptionIndex(opts, 'Fall 2026-2027'), 1);
  assert.equal(matchOptionIndex(opts, 'Spring 2025-2026'), 0);
});

test('matchOptionIndex does not match a mere prefix', () => {
  assert.equal(matchOptionIndex(['Fall 2026-20270'], 'Fall 2026-2027'), -1);
  assert.equal(matchOptionIndex([], 'x'), -1);
});

test('sanitizeForPath matches the existing file naming', () => {
  assert.equal(sanitizeForPath('Abbasi, Nazneen'), 'Abbasi Nazneen');
  assert.equal(sanitizeForPath('Math: K/12?'), 'Math- K-12-');
});

test('substitute fills vars and cleans only path vars', () => {
  const vars = { term: 'Fall 2026-2027', teacher: 'Abbasi, Nazneen' };
  assert.equal(substitute('${term}/${teacher}', vars), 'Fall 2026-2027/Abbasi, Nazneen');
  assert.equal(substitute('${term}/${teacher}.csv', vars, new Set(['teacher'])), 'Fall 2026-2027/Abbasi Nazneen.csv');
});

test('substitute throws on an unknown variable', () => {
  assert.throws(() => substitute('${nope}', {}), /Unknown variable \$\{nope\}/);
});

test('nthIndexOf finds the n-th occurrence of a name', async () => {
  const { nthIndexOf } = await import('../src/engine.ts');
  assert.equal(nthIndexOf(['a', 'b', 'a'], 'a', 0), 0);
  assert.equal(nthIndexOf(['a', 'b', 'a'], 'a', 1), 2);
  assert.equal(nthIndexOf(['a', 'b', 'a'], 'a', 2), -1);
});

test('selectItems drops blanks, skip names and untested seasons, numbering duplicates', async () => {
  const { selectItems } = await import('../src/engine.ts');
  assert.deepEqual(selectItems(['x', '', 'y', 'x', 'z'], new Set(['z'])), [
    { name: 'x', occurrence: 0 }, { name: 'y', occurrence: 0 }, { name: 'x', occurrence: 1 },
  ]);
  assert.deepEqual(selectItems(['Fall 2026 - Winter 2027 Report', 'Fall 2026 - Spring 2027 Report'], new Set(), ['Fall', 'Spring']),
    [{ name: 'Fall 2026 - Spring 2027 Report', occurrence: 0 }]);
});
