"use client";

import { useI18n } from "@/lib/i18n/client";
import { es } from "@/lib/i18n/es";

/** The exact wording required by the requirements (§12). Keep it in sync with the reporter app. */
export const PROTOTYPE_BANNER_TEXT = es.banner;

/**
 * Shown at the top of every staff page until the project leaves Phase 0. Always in Spanish, as
 * the requirements word it; in another language its translation follows, as on the reporter app.
 */
export function PrototypeBanner() {
  const { lang, t } = useI18n();
  return (
    <div
      role="note"
      data-testid="prototype-banner"
      className="shrink-0 border-b border-amber-300 bg-amber-100 px-4 py-2 text-center text-sm font-medium text-amber-950 dark:border-amber-700 dark:bg-amber-950 dark:text-amber-100"
    >
      <span lang="es">{PROTOTYPE_BANNER_TEXT}</span>
      {lang !== "es" && <span className="block font-normal">{t.banner}</span>}
    </div>
  );
}
