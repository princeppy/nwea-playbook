import { test } from 'node:test';
import assert from 'node:assert/strict';
import { launchArgs } from '../src/launch.ts';

const BUNDLED = '/pkg/nwea-class-profile.yaml';

test('launchArgs uses the bundled playbook and turns a bare first argument into term=', () => {
  assert.deepEqual(launchArgs(['Spring 2025-2026', '--headed'], BUNDLED), [
    BUNDLED,
    'term=Spring 2025-2026',
    '--headed',
  ]);
  assert.deepEqual(launchArgs([], BUNDLED), [BUNDLED]);
});

test('launchArgs keeps a playbook, flags and name=value pairs as given', () => {
  assert.deepEqual(launchArgs(['my.yaml', 'Fall 2026-2027'], BUNDLED), ['my.yaml', 'Fall 2026-2027']);
  assert.deepEqual(launchArgs(['--keep', 'term=Fall 2026-2027'], BUNDLED), [
    BUNDLED,
    '--keep',
    'term=Fall 2026-2027',
  ]);
  assert.deepEqual(launchArgs(['pass=x.yml'], BUNDLED), [BUNDLED, 'pass=x.yml']);
});
