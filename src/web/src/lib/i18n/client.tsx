"use client";

import { createContext, useCallback, useContext, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import {
  DEFAULT_LANG, LANG_COOKIE, LANG_COOKIE_MAX_AGE, formatAgo, formatDate, formatDateTime, messagesFor,
  type Lang, type Messages,
} from "./languages.ts";

interface I18n {
  lang: Lang;
  t: Messages;
  setLang: (lang: Lang) => void;
  /** Date and time, date only, and "how long ago", in the current language. */
  dateTime: (iso: string | null | undefined) => string;
  date: (iso: string | null | undefined) => string;
  ago: (iso: string | null | undefined) => string;
}

const I18nContext = createContext<I18n | null>(null);

/** The current language's texts and formatters. */
export function useI18n(): I18n {
  const value = useContext(I18nContext);
  if (!value) throw new Error("useI18n needs a LanguageProvider above it.");
  return value;
}

/**
 * Holds the language the server rendered with (from the cookie) and switches it: the cookie is
 * updated, the page re-renders in place, and the server parts (titles) are refreshed.
 */
export function LanguageProvider({ initial = DEFAULT_LANG, children }: { initial?: Lang; children: React.ReactNode }) {
  const router = useRouter();
  const [lang, setLangState] = useState<Lang>(initial);

  const setLang = useCallback((next: Lang) => {
    const secure = window.location.protocol === "https:" ? "; Secure" : "";
    document.cookie = `${LANG_COOKIE}=${next}; Path=/; Max-Age=${LANG_COOKIE_MAX_AGE}; SameSite=Lax${secure}`;
    document.documentElement.lang = next;
    setLangState(next);
    router.refresh();
  }, [router]);

  const value = useMemo<I18n>(() => {
    const t = messagesFor(lang);
    const locale = t.language.locale;
    return {
      lang,
      t,
      setLang,
      dateTime: (iso) => formatDateTime(iso, locale),
      date: (iso) => formatDate(iso, locale),
      ago: (iso) => formatAgo(iso, locale),
    };
  }, [lang, setLang]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}
