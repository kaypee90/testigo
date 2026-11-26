/** The exact wording required by the requirements (§12). Keep it in sync with the reporter app. */
export const PROTOTYPE_BANNER_TEXT =
  "Prototipo con datos ficticios. No es un canal oficial de denuncia. No envíe información real.";

/** Shown at the top of every staff page until the project leaves Phase 0. */
export function PrototypeBanner() {
  return (
    <div
      role="note"
      data-testid="prototype-banner"
      className="shrink-0 border-b border-amber-300 bg-amber-100 px-4 py-2 text-center text-sm font-medium text-amber-950 dark:border-amber-700 dark:bg-amber-950 dark:text-amber-100"
    >
      {PROTOTYPE_BANNER_TEXT}
    </div>
  );
}
