import { test } from 'node:test';
import assert from 'node:assert/strict';
import { PassThrough } from 'node:stream';
import { askForSecrets, isHidden, terminalAsk } from '../src/prompt.ts';

test('askForSecrets asks for each name in order, hiding passwords, and drops empty answers', async () => {
  const asked: string[] = [];
  const answers: Record<string, string> = { 'NWEA_USER: ': 'me@school.org', 'NWEA_PASS: ': '' };
  const got = await askForSecrets(['NWEA_USER', 'NWEA_PASS'], async (q, hidden) => {
    asked.push(`${q}${hidden ? '(hidden)' : ''}`);
    return answers[q] ?? '';
  });
  assert.deepEqual(asked, ['NWEA_USER: ', 'NWEA_PASS: (hidden)']);
  assert.deepEqual(got, { NWEA_USER: 'me@school.org' });
});

test('isHidden covers passwords, secrets, tokens and keys', () => {
  assert.deepEqual(['NWEA_PASS', 'API_TOKEN', 'CLIENT_SECRET', 'API_KEY', 'NWEA_USER'].map(isHidden), [
    true, true, true, true, false,
  ]);
});

test('terminalAsk shows a question and its answer, but never echoes a hidden answer', async () => {
  const run = async (hidden: boolean): Promise<{ answer: string; shown: string }> => {
    const input = new PassThrough();
    const output = new PassThrough();
    let shown = '';
    output.on('data', (d: Buffer) => (shown += d.toString()));
    const pending = terminalAsk(input, output)('NWEA_PASS: ', hidden);
    input.write(' p#ss word \r\n');
    return { answer: await pending, shown };
  };
  const hidden = await run(true);
  assert.equal(hidden.answer, ' p#ss word ', 'kept exactly, spaces included');
  assert.match(hidden.shown, /NWEA_PASS: /);
  assert.doesNotMatch(hidden.shown, /p#ss/);
  assert.match((await run(false)).shown, /p#ss/);
});
