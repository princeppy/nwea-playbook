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

export function terminalAsk(
  input: NodeJS.ReadableStream = process.stdin,
  output: NodeJS.WritableStream = process.stdout,
): Ask {
  return (question, hidden) =>
    new Promise((resolve) => {
      const rl = createInterface({ input, output, terminal: true });
      if (hidden) {
        output.write(question);
        // readline echoes typed characters through this method; silence it for passwords
        (rl as unknown as { _writeToOutput: (s: string) => void })._writeToOutput = () => {};
      }
      rl.on('SIGINT', () => {
        rl.close();
        output.write('\n');
        process.exit(EXIT_INTERRUPTED);
      });
      rl.question(hidden ? '' : question, (answer) => {
        rl.close();
        if (hidden) output.write('\n');
        resolve(answer);
      });
    });
}
