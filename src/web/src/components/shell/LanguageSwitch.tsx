"use client";

import { cx } from "@/components/ui";
import { useI18n } from "@/lib/i18n/client";
import { LANGUAGES, type Lang } from "@/lib/i18n/languages";

/** Español | English. Each option is named in its own language, so anyone can find theirs. */
export function LanguageSwitch({ className }: { className?: string }) {
  const { lang, t, setLang } = useI18n();
  return (
    <div className={cx("flex items-center justify-between gap-2 rounded-md px-2 py-1", className)}>
      <span className="text-xs text-zinc-500">{t.language.label}</span>
      <div className="flex rounded-md bg-zinc-200/60 p-0.5 dark:bg-zinc-900" role="group" aria-label={t.language.label}>
        {(Object.keys(LANGUAGES) as Lang[]).map((code) => (
          <button
            key={code}
            type="button"
            lang={code}
            title={LANGUAGES[code].language.name}
            aria-label={LANGUAGES[code].language.name}
            aria-pressed={lang === code}
            onClick={() => lang !== code && setLang(code)}
            className={cx(
              "rounded px-1.5 py-0.5 text-[11px] font-semibold uppercase transition-colors",
              lang === code ? "bg-white text-zinc-900 shadow-sm dark:bg-zinc-700 dark:text-zinc-50" : "text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200",
            )}
          >
            {code}
          </button>
        ))}
      </div>
    </div>
  );
}
