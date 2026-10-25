// NWEA terms ("Fall 2026-2027") and the seasons a school tests in.

export const SEASONS = ['Fall', 'Winter', 'Spring', 'Summer'] as const;
export type Season = (typeof SEASONS)[number];

export interface Term {
  readonly label: string;
  readonly season: Season;
  readonly startYear: number;
  readonly endYear: number;
}

const TERM_PATTERN = /^(Fall|Winter|Spring|Summer) (\d{4})-(\d{4})$/;
const SEASON_WORD = /\b(Fall|Winter|Spring|Summer)\b/g;

const isSeason = (s: string): s is Season => (SEASONS as readonly string[]).includes(s);

/** Parse "Fall, Spring" (or "Fall Spring") into a season list. Unknown words throw. */
export function parseSeasons(text: string): Season[] {
  const words = text.split(/[\s,]+/).filter(Boolean);
  if (words.length === 0) throw new Error('testedSeasons is empty. List at least one of: ' + SEASONS.join(', '));
  const bad = words.filter((w) => !isSeason(w));
  if (bad.length) throw new Error(`Unknown season(s) in testedSeasons: ${bad.join(', ')}. Use: ${SEASONS.join(', ')}`);
  return [...new Set(words as Season[])];
}

/** Validate a term label against the school's tested seasons. Throws a readable error. */
export function parseTerm(label: string, tested: readonly Season[]): Term {
  const m = label.trim().match(TERM_PATTERN);
  if (!m) {
    throw new Error(`Term "${label}" is not valid. Write it as NWEA shows it, e.g. "Fall 2026-2027" or "Spring 2025-2026".`);
  }
  const season = m[1] as Season;
  const startYear = Number(m[2]);
  const endYear = Number(m[3]);
  if (endYear !== startYear + 1) {
    throw new Error(`Term "${label}": the academic years must be consecutive, e.g. ${startYear}-${startYear + 1}.`);
  }
  if (!tested.includes(season)) {
    throw new Error(`Term "${label}": this school only tests in ${tested.join(' and ')} (testedSeasons).`);
  }
  return { label: m[0], season, startYear, endYear };
}

/** Every season named in a text, e.g. "Fall 2026 - Winter 2027 ..." -> [Fall, Winter]. */
export function seasonsIn(text: string): Season[] {
  return [...text.matchAll(SEASON_WORD)].map((m) => m[1] as Season);
}

/** A growth comparison period is kept only if every season it names is tested. */
export function periodAllowed(label: string, tested: readonly Season[]): boolean {
  return seasonsIn(label).every((s) => tested.includes(s));
}
