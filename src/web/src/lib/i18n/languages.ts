import { es, type Messages } from "./es.ts";
import { en } from "./en.ts";

/**
 * The dashboard's languages. Spanish is the default and the source; the choice is kept in a
 * first-party cookie (no personal data: just the code) so pages render in it from the server.
 * Keep this file free of framework imports: the catalog test loads it straight into Node.
 */
export const LANGUAGES = { es, en } satisfies Record<string, Messages>;

export type Lang = keyof typeof LANGUAGES;

export const DEFAULT_LANG: Lang = "es";

/** The cookie that remembers the choice for a year. */
export const LANG_COOKIE = "testigo_lang";
export const LANG_COOKIE_MAX_AGE = 60 * 60 * 24 * 365;

/** A supported language code, or the default for anything else (a missing or tampered cookie). */
export function parseLang(value: string | null | undefined): Lang {
  return value && Object.prototype.hasOwnProperty.call(LANGUAGES, value) ? (value as Lang) : DEFAULT_LANG;
}

export function messagesFor(lang: Lang): Messages {
  return LANGUAGES[lang];
}

/** A date and time in the language's own format, or a dash. */
export function formatDateTime(iso: string | null | undefined, locale: string): string {
  return iso ? new Date(iso).toLocaleString(locale, { dateStyle: "medium", timeStyle: "short" }) : "—";
}

/** A date in the language's own format, or a dash. */
export function formatDate(iso: string | null | undefined, locale: string): string {
  return iso ? new Date(iso).toLocaleDateString(locale) : "—";
}

/** How long ago, in the language ("hace 5 min", "5 min ago"), or a dash. */
export function formatAgo(iso: string | null | undefined, locale: string, now: number = Date.now()): string {
  if (!iso) return "—";
  const s = Math.max(0, (now - new Date(iso).getTime()) / 1000);
  const rtf = new Intl.RelativeTimeFormat(locale, { numeric: "auto", style: "short" });
  if (s < 60) return rtf.format(0, "second");
  if (s < 3600) return rtf.format(-Math.floor(s / 60), "minute");
  if (s < 86400) return rtf.format(-Math.floor(s / 3600), "hour");
  return rtf.format(-Math.floor(s / 86400), "day");
}

export type { Messages };
