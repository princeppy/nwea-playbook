// Small text helpers shared by the runner.

/** Collapse runs of whitespace (including &nbsp;) to one space and trim. */
export const normalize = (s: string): string => s.replace(/\s+/g, ' ').trim();

/**
 * Index of the option that matches `value`: an exact match first, otherwise an
 * option that is `value` followed by a bracketed note, such as
 * "Fall 2026-2027 (Most Recent)" for "Fall 2026-2027". Returns -1 if none match.
 */
export function matchOptionIndex(options: readonly string[], value: string): number {
  const wanted = normalize(value);
  const names = options.map(normalize);
  const exact = names.indexOf(wanted);
  if (exact >= 0) return exact;
  return names.findIndex((n) => n.startsWith(`${wanted} (`));
}
