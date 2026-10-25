// ${name} substitution and file-name cleaning.

export type Vars = Readonly<Record<string, string>>;

const UNSAFE_PATH_CHARS = /[\\/:*?"<>|]+/g;

/** Make a value safe as part of a file name: "Abbasi, Nazneen" -> "Abbasi Nazneen". */
export function sanitizeForPath(value: string): string {
  return value.replace(UNSAFE_PATH_CHARS, '-').replace(/,/g, '').replace(/\s+/g, ' ').trim();
}

/**
 * Replace every ${name} in `text` with its value. Names listed in `pathVars`
 * are cleaned for use in file names. An unknown name throws.
 */
export function substitute(text: unknown, vars: Vars, pathVars?: ReadonlySet<string>): string {
  return String(text).replace(/\$\{(\w+)\}/g, (_m, name: string) => {
    if (!(name in vars)) throw new Error(`Unknown variable \${${name}}`);
    const value = vars[name];
    return pathVars?.has(name) ? sanitizeForPath(value) : value;
  });
}
