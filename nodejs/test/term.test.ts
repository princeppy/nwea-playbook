import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseSeasons, parseTerm, periodAllowed, seasonsIn } from '../src/term.ts';

const FALL_SPRING = ['Fall', 'Spring'] as const;

test('parseSeasons accepts comma or space separated seasons', () => {
  assert.deepEqual(parseSeasons('Fall, Spring'), ['Fall', 'Spring']);
  assert.deepEqual(parseSeasons('Fall Winter Spring Summer'), ['Fall', 'Winter', 'Spring', 'Summer']);
  assert.deepEqual(parseSeasons('Fall, Fall'), ['Fall']);
});

test('parseSeasons rejects unknown or empty input', () => {
  assert.throws(() => parseSeasons('Autumn'), /Unknown season/);
  assert.throws(() => parseSeasons(' '), /empty/);
});

test('parseTerm accepts NWEA term labels for tested seasons', () => {
  assert.deepEqual(parseTerm('Fall 2026-2027', FALL_SPRING), {
    label: 'Fall 2026-2027', season: 'Fall', startYear: 2026, endYear: 2027,
  });
  assert.equal(parseTerm(' Spring 2025-2026 ', FALL_SPRING).season, 'Spring');
});

test('parseTerm rejects malformed labels', () => {
  assert.throws(() => parseTerm('Fall 2026', FALL_SPRING), /not valid/);
  assert.throws(() => parseTerm('fall 2026-2027', FALL_SPRING), /not valid/);
  assert.throws(() => parseTerm('Fall 2026-2027 (Most Recent)', FALL_SPRING), /not valid/);
});

test('parseTerm rejects non-consecutive years', () => {
  assert.throws(() => parseTerm('Fall 2026-2028', FALL_SPRING), /consecutive/);
});

test('parseTerm rejects seasons the school does not test', () => {
  assert.throws(() => parseTerm('Winter 2026-2027', FALL_SPRING), /only tests in Fall and Spring/);
  assert.equal(parseTerm('Winter 2026-2027', ['Fall', 'Winter', 'Spring']).season, 'Winter');
});

test('seasonsIn lists every season named in a period label', () => {
  assert.deepEqual(seasonsIn('Fall 2026 - Winter 2027 Growth Projection Report'), ['Fall', 'Winter']);
  assert.deepEqual(seasonsIn('No seasons here'), []);
});

test('periodAllowed keeps only periods made of tested seasons', () => {
  const fall = [
    'Fall 2026 - Winter 2027 Growth Projection Report',
    'Fall 2026 - Spring 2027 Growth Projection Report',
    'Fall 2026 - Fall 2027 Growth Projection Report',
    'Fall 2025 - Fall 2026 Growth Summary Report',
  ];
  const spring = [
    'Spring 2026 - Spring 2027 Growth Projection Report',
    'Fall 2025 - Spring 2026 Growth Summary Report',
    'Winter 2026 - Spring 2026 Growth Summary Report',
    'Spring 2025 - Spring 2026 Growth Summary Report',
  ];
  assert.deepEqual(fall.filter((p) => periodAllowed(p, FALL_SPRING)), fall.slice(1));
  assert.deepEqual(spring.filter((p) => periodAllowed(p, FALL_SPRING)), [spring[0], spring[1], spring[3]]);
  assert.equal(fall.filter((p) => periodAllowed(p, ['Fall', 'Winter', 'Spring', 'Summer'])).length, 4);
});
