// Asks at the terminal for secrets that are neither in .env nor on the command line,
// e.g. a first run of the nwea-playbook command in a folder without a .env file.
// Passwords are not echoed. Only used when someone is at the terminal (see run.ts).

import { createInterface } from 'node:readline';

export type Ask = (question: string, hidden: boolean) => Promise<string>;

const HIDDEN_NAME = /PASS|SECRET|TOKEN|KEY/i;
const EXIT_INTERRUPTED = 130;

export const isHidden = (name: string): boolean => HIDDEN_NAME.test(name);

/** One question per name, in order; names left empty are not in the result. */
export async function askForSecrets(names: readonly string[], ask: Ask): Promise<Record<string, string>> {
  const answers: Array<readonly [string, string]> = [];
  for (const name of names) answers.push([name, await ask(`${name}: `, isHidden(name))]);
  return Object.fromEntries(answers.filter(([, v]) => v));
}

export interface TerminalPrompt {
  readonly ask: Ask;
  readonly close: () => void;
}

/**
 * One readline interface for every question: a second interface on the same stdin can lose
 * input typed quickly or pasted, and redraws over its own question.
 */
export function terminalAsk(
  input: NodeJS.ReadableStream = process.stdin,
  output: NodeJS.WritableStream = process.stdout,
): TerminalPrompt {
  const rl = createInterface({ input, output, terminal: true });
  const typed: string[] = [];
  const waiting: Array<{ readonly resolve: (line: string) => void; readonly reject: (e: Error) => void }> = [];
  let closed = false;
  const cancelled = (question: string): Error => new Error(`Cancelled: no answer for ${question.replace(/:\s*$/, '')}`);
  let current: { readonly question: string; readonly hidden: boolean } | undefined;

  // readline writes the prompt and every typed character through this method. Nothing typed
  // ahead of a question is shown (it may be a password); for a hidden answer only the prompt is.
  const internal = rl as unknown as { _writeToOutput: (s: string) => void };
  const write = internal._writeToOutput.bind(rl);
  internal._writeToOutput = (s) => {
    if (!current) return;
    if (!current.hidden) write(s);
    else if (s.startsWith(current.question)) write(current.question);
  };

  rl.on('line', (line) => {
    const next = waiting.shift();
    if (next) next.resolve(line);
    else typed.push(line);
  });
  // input ended (Ctrl+D, or stdin closed): fail the question rather than leave it waiting,
  // which would let Node exit silently with code 0
  rl.on('close', () => {
    closed = true;
    if (waiting.length) output.write('\n');
    waiting.splice(0).forEach((w) => w.reject(cancelled(current?.question ?? '')));
  });
  rl.on('SIGINT', () => {
    rl.close();
    process.exit(EXIT_INTERRUPTED);
  });

  const ask: Ask = async (question, hidden) => {
    const early = typed.shift();
    if (early === undefined && closed) throw cancelled(question);
    current = { question, hidden };
    rl.setPrompt(question);
    rl.prompt();
    let answer: string;
    try {
      answer = early ?? (await new Promise<string>((resolve, reject) => waiting.push({ resolve, reject })));
    } finally {
      current = undefined;
    }
    // an answer typed ahead was not echoed: show it now (unless hidden) and end the line
    if (hidden || early !== undefined) output.write(`${early !== undefined && !hidden ? early : ''}\n`);
    return answer;
  };
  return { ask, close: () => rl.close() };
}
