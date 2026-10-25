// Console + runs/<timestamp>/run.log output. Every line is masked before it is written.

import { appendFileSync } from 'node:fs';

export interface Logger {
  readonly file: string;
  info(message: string, depth?: number): void;
  error(message: string): void;
}

export function createLogger(file: string, mask: (s: string) => string): Logger {
  const write = (line: string, stream: NodeJS.WriteStream): void => {
    const masked = mask(line);
    stream.write(masked + '\n');
    try {
      appendFileSync(file, masked + '\n');
    } catch (e: unknown) {
      process.stderr.write(`(could not write ${file}: ${e instanceof Error ? e.message : String(e)})\n`);
    }
  };
  return {
    file,
    info: (message, depth = 0) => write('  '.repeat(depth) + message, process.stdout),
    error: (message) => write(message, process.stderr),
  };
}

/** First line of an error message, for compact logs. */
export const errorLine = (e: unknown): string => String(e instanceof Error ? e.message : e).split('\n')[0];
