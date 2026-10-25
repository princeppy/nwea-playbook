// Command-line arguments: run.ts <playbook.yaml> [--headed] [--keep] [--slow=300] [name=value ...]

export interface CliArgs {
  readonly file?: string;
  readonly headed: boolean;
  readonly keep: boolean;
  readonly slowMo: number;
  readonly vars: Readonly<Record<string, string>>;
}

const HEADED_SLOW_MO = 200;

export function parseArgs(argv: readonly string[]): CliArgs {
  // name=value pairs are never the playbook, even if the value ends in .yml (e.g. a password)
  const file = argv.find((a) => !a.includes('=') && /\.ya?ml$/i.test(a));
  const keep = argv.includes('--keep');
  const headed = keep || argv.includes('--headed');
  const slowArg = argv.find((a) => a.startsWith('--slow='));
  const slowMo = slowArg ? Number(slowArg.split('=')[1]) || 0 : headed ? HEADED_SLOW_MO : 0;
  const vars = Object.fromEntries(
    argv.flatMap((a) => {
      const m = a.match(/^(\w+)=(.*)$/s);
      return m ? [[m[1], m[2]]] : [];
    }),
  );
  return { file, headed, keep, slowMo, vars };
}

export const USAGE =
  'Usage: npx tsx run.ts <playbook.yaml> [--headed] [--keep] [--slow=300] [name=value ...]\n' +
  '  e.g. npx tsx run.ts nwea-class-profile.yaml term="Fall 2026-2027"';
