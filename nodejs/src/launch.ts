// Arguments for the nwea-playbook command (bin/nwea-playbook.mjs), which runs from any folder:
// the repo's playbook is used unless one is given, and a bare first argument is the term,
// exactly as run.sh does it. `nwea-playbook "Spring 2025-2026" --headed`

const isPlaybook = (a: string): boolean => !a.includes('=') && /\.ya?ml$/i.test(a);
const isTerm = (a: string): boolean => !a.startsWith('-') && !a.includes('=') && !isPlaybook(a);

export function launchArgs(argv: readonly string[], bundledPlaybook: string): string[] {
  if (argv.some(isPlaybook)) return [...argv];
  const [first, ...rest] = argv;
  return first !== undefined && isTerm(first)
    ? [bundledPlaybook, `term=${first}`, ...rest]
    : [bundledPlaybook, ...argv];
}
