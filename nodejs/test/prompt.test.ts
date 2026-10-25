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

function fakeTerminal(): { input: PassThrough; output: PassThrough; shown: () => string } {
  const input = new PassThrough();
  const output = new PassThrough();
  let shown = '';
  output.on('data', (d: Buffer) => (shown += d.toString()));
  return { input, output, shown: () => shown };
}

// What a terminal shows once cursor moves and line clears are applied, line by line.
const screen = (raw: string): string[] =>
  raw
    .split(/\r?\n/)
    .map((line) => line.split(/\x1b\[1G\x1b\[0[JK]/).pop()!.replace(/\x1b\[[0-9;]*[A-Za-z]/g, ''));

test('terminalAsk reads answers typed in one burst and never shows a hidden answer', async () => {
  const t = fakeTerminal();
  const prompt = terminalAsk(t.input, t.output);
  t.input.write('me@school.org\r p#ss word \r');
  const user = await prompt.ask('NWEA_USER: ', false);
  const pass = await prompt.ask('NWEA_PASS: ', true);
  prompt.close();
  assert.equal(user, 'me@school.org');
  assert.equal(pass, ' p#ss word ', 'kept exactly, spaces included');
  assert.doesNotMatch(t.shown(), /p#ss/);
  assert.ok(screen(t.shown()).includes('NWEA_PASS: '), `password label stays on screen: ${JSON.stringify(t.shown())}`);
  assert.ok(screen(t.shown()).some((l) => l.includes('NWEA_USER: me@school.org')));
});

test('terminalAsk fails instead of waiting forever when the input ends (Ctrl+D)', async () => {
  const t = fakeTerminal();
  const prompt = terminalAsk(t.input, t.output);
  const pending = prompt.ask('NWEA_USER: ', false);
  t.input.end();
  await assert.rejects(pending, /Cancelled: no answer for NWEA_USER/);
  await assert.rejects(prompt.ask('NWEA_PASS: ', true), /Cancelled: no answer for NWEA_PASS/);
});

test('terminalAsk waits for an answer typed after the question', async () => {
  const t = fakeTerminal();
  const prompt = terminalAsk(t.input, t.output);
  const pending = prompt.ask('NWEA_PASS: ', true);
  setTimeout(() => t.input.write('later\r'), 20);
  assert.equal(await pending, 'later');
  prompt.close();
});
