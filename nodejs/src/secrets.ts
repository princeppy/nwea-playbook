// Secrets come from .env, or from a command-line argument named in the playbook:
//
//   secrets:
//     NWEA_USER: { arg: user }   # user=... on the command line overrides .env
//     NWEA_PASS: { arg: pass }
//
// The older list form, secrets: [NWEA_USER, NWEA_PASS], still works.

export interface SecretSpec {
  readonly name: string;
  readonly arg?: string;
}

export function normalizeSecretSpecs(raw: unknown): SecretSpec[] {
  if (!raw) return [];
  if (Array.isArray(raw)) return raw.map((name) => ({ name: String(name) }));
  if (typeof raw === 'object') {
    return Object.entries(raw as Record<string, { arg?: unknown } | null>).map(([name, v]) => ({
      name,
      arg: v && v.arg !== undefined ? String(v.arg) : undefined,
    }));
  }
  throw new Error('"secrets" must be a list of names or a map of name: { arg }');
}

export interface ResolvedSecrets {
  readonly values: Readonly<Record<string, string>>;
  readonly missing: readonly string[];
}

/** Look each secret up on the command line first, then in the environment. */
export function resolveSecrets(
  specs: readonly SecretSpec[],
  cli: Readonly<Record<string, string>>,
  env: Readonly<Record<string, string | undefined>>,
): ResolvedSecrets {
  const found = specs.map((s) => [s.name, (s.arg && cli[s.arg]) || env[s.name] || ''] as const);
  return {
    values: Object.fromEntries(found.filter(([, v]) => v)),
    missing: found.filter(([, v]) => !v).map(([name]) => name),
  };
}

/** CLI vars without the secret arguments, so secrets never become ${vars}. */
export function withoutSecretArgs(
  cli: Readonly<Record<string, string>>,
  specs: readonly SecretSpec[],
): Record<string, string> {
  const secretArgs = new Set(specs.flatMap((s) => (s.arg ? [s.arg] : [])));
  return Object.fromEntries(Object.entries(cli).filter(([k]) => !secretArgs.has(k)));
}

/** Returns a function that replaces every secret value in a string with ***. */
export function makeMasker(values: readonly string[]): (s: string) => string {
  const secrets = values.filter(Boolean);
  return (s) => secrets.reduce((t, v) => t.split(v).join('***'), s);
}
