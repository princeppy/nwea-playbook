import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseArgs } from '../src/args.ts';
import { resolveParams } from '../src/params.ts';
import { makeMasker, normalizeSecretSpecs, resolveSecrets, withoutSecretArgs } from '../src/secrets.ts';

test('parseArgs reads file, flags and name=value pairs', () => {
  const a = parseArgs(['nwea.yaml', '--headed', 'term=Fall 2026-2027', 'pass=a=b']);
  assert.equal(a.file, 'nwea.yaml');
  assert.equal(a.headed, true);
  assert.equal(a.slowMo, 200);
  assert.deepEqual(a.vars, { term: 'Fall 2026-2027', pass: 'a=b' });
});

test('parseArgs: --keep implies headed; --slow overrides', () => {
  assert.equal(parseArgs(['x.yml', '--keep']).headed, true);
  assert.equal(parseArgs(['x.yml', '--slow=50']).slowMo, 50);
  assert.equal(parseArgs([]).file, undefined);
  assert.equal(parseArgs(['x.yml']).slowMo, 0);
  assert.equal(parseArgs(['pass=secret.yml', 'x.yml']).file, 'x.yml');
});

test('resolveParams applies defaults, lists and CLI overrides', () => {
  const spec = {
    term: { required: true, describe: 'e.g. "Fall 2026-2027"' },
    testedSeasons: ['Fall', 'Spring'],
    downloadDir: 'downloads/nwea',
    limit: { default: 3 },
  };
  assert.deepEqual(resolveParams(spec, { term: 'Fall 2026-2027', extra: 'x' }), {
    testedSeasons: 'Fall, Spring', downloadDir: 'downloads/nwea', limit: '3', term: 'Fall 2026-2027', extra: 'x',
  });
});

test('resolveParams reads env: values; the command line still wins', () => {
  const spec = { term: { required: true, env: 'NWEA_TERM' } };
  assert.deepEqual(resolveParams(spec, {}, { NWEA_TERM: 'Fall 2026-2027' }), { term: 'Fall 2026-2027' });
  assert.deepEqual(resolveParams(spec, { term: 'Spring 2025-2026' }, { NWEA_TERM: 'Fall 2026-2027' }), { term: 'Spring 2025-2026' });
  assert.throws(() => resolveParams(spec, {}, {}), /term, or NWEA_TERM= in \.env/);
});

test('resolveParams reports missing required params with their description', () => {
  assert.throws(() => resolveParams({ term: { required: true, describe: 'season + year' } }, {}),
    /Missing required input\(s\): term \(season \+ year\)/);
  assert.deepEqual(resolveParams(undefined, {}), {});
});

test('secrets: list and map forms', () => {
  assert.deepEqual(normalizeSecretSpecs(['A']), [{ name: 'A' }]);
  assert.deepEqual(normalizeSecretSpecs({ A: { arg: 'user' }, B: null }), [{ name: 'A', arg: 'user' }, { name: 'B', arg: undefined }]);
  assert.deepEqual(normalizeSecretSpecs(undefined), []);
  assert.throws(() => normalizeSecretSpecs('A'), /must be/);
});

test('secrets: CLI arg wins over env; missing ones are reported', () => {
  const specs = normalizeSecretSpecs({ NWEA_USER: { arg: 'user' }, NWEA_PASS: { arg: 'pass' } });
  const r = resolveSecrets(specs, { user: 'cli@x' }, { NWEA_USER: 'env@x' });
  assert.deepEqual(r.values, { NWEA_USER: 'cli@x' });
  assert.deepEqual(r.missing, ['NWEA_PASS']);
  assert.deepEqual(withoutSecretArgs({ user: 'u', pass: 'p', term: 't' }, specs), { term: 't' });
});

test('makeMasker hides every secret value', () => {
  const mask = makeMasker(['s3cret', '']);
  assert.equal(mask('pw=s3cret and s3cret'), 'pw=*** and ***');
});
