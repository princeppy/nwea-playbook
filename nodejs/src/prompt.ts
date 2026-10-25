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
  const waiting: Array<(line: string) => void> = [];
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
    if (next) next(line);
    else typed.push(line);
  });
  rl.on('SIGINT', () => {
    rl.close();
    output.write('\n');
    process.exit(EXIT_INTERRUPTED);
  });

  const ask: Ask = async (question, hidden) => {
    current = { question, hidden };
    rl.setPrompt(question);
    rl.prompt();
    const early = typed.shift();
    const answer = early ?? (await new Promise<string>((resolve) => waiting.push(resolve)));
    current = undefined;
    // an answer typed ahead was not echoed: show it now (unless hidden) and end the line
    if (hidden || early !== undefined) output.write(`${early !== undefined && !hidden ? early : ''}\n`);
    return answer;
  };
  return { ask, close: () => rl.close() };
}
