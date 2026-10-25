// Playbook params. Each value comes from, in order: name=value on the command line,
// the environment variable named by `env:` (e.g. from .env), then the YAML default.
//
//   params:
//     term: { required: true, env: NWEA_TERM, describe: 'e.g. "Fall 2026-2027"' }
//     testedSeasons: [Fall, Spring]      # a list becomes "Fall, Spring"
//     downloadDir: downloads/nwea        # a plain default

interface ParamSpec {
  readonly required?: boolean;
  readonly env?: string;
  readonly default?: unknown;
  readonly describe?: string;
}

const toText = (v: unknown): string => (Array.isArray(v) ? v.map(String).join(', ') : String(v));

const isSpec = (v: unknown): v is ParamSpec =>
  typeof v === 'object' &&
  v !== null &&
  !Array.isArray(v) &&
  ('required' in v || 'default' in v || 'describe' in v || 'env' in v);

/** Merge YAML params, environment and CLI values into string vars. Missing required params throw. */
export function resolveParams(
  spec: unknown,
  cli: Readonly<Record<string, string>>,
  env: Readonly<Record<string, string | undefined>> = {},
): Record<string, string> {
  const entries = Object.entries((spec ?? {}) as Record<string, unknown>);
  const missing: string[] = [];
  const fromYaml = entries.flatMap(([name, raw]): [string, string][] => {
    if (name in cli) return [];
    if (!isSpec(raw)) return raw === null || raw === undefined ? [] : [[name, toText(raw)]];
    const fromEnv = raw.env ? env[raw.env] : undefined;
    if (fromEnv) return [[name, fromEnv]];
    if (raw.default !== undefined) return [[name, toText(raw.default)]];
    if (raw.required) {
      const where = raw.env ? `, or ${raw.env}= in .env` : '';
      missing.push(`${name}${raw.describe ? ` (${raw.describe}${where})` : where}`);
    }
    return [];
  });
  if (missing.length) {
    throw new Error(`Missing required input(s): ${missing.join('; ')}. Pass them as name="value".`);
  }
  return { ...Object.fromEntries(fromYaml), ...cli };
}
