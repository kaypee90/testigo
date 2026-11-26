"use client";

import { en } from "@/lib/i18n/en";
import { es } from "@/lib/i18n/es";

/**
 * Shown when a page fails to render and nothing closer caught it. It replaces the root layout (and
 * the language provider with it), so it brings its own document and styles and says it in both
 * languages. Nothing is reported anywhere: errors could carry case content (NFR-OPS-4).
 */
export default function GlobalError({ retry }: { error: unknown; retry: () => void }) {
  return (
    <html lang="es" style={{ colorScheme: "light dark" }}>
      <body style={{ fontFamily: "system-ui, sans-serif", margin: 0, minHeight: "100vh", display: "grid", placeItems: "center" }}>
        <title>{`${es.globalError.title} / ${en.globalError.title}`}</title>
        <main style={{ maxWidth: "28rem", padding: "1.5rem", textAlign: "center" }}>
          {[es, en].map((t) => (
            <section key={t.language.name} lang={t === es ? "es" : "en"}>
              <h1 style={{ fontSize: "1.25rem", fontWeight: 600 }}>{t.globalError.title}</h1>
              <p style={{ opacity: 0.75, lineHeight: 1.5 }}>{t.globalError.body}</p>
            </section>
          ))}
          <button
            onClick={() => retry()}
            style={{ marginTop: "0.75rem", padding: "0.5rem 1rem", borderRadius: "0.375rem", border: "1px solid currentColor", background: "transparent", color: "inherit", font: "inherit", cursor: "pointer" }}
          >
            {es.globalError.retry} / <span lang="en">{en.globalError.retry}</span>
          </button>
        </main>
      </body>
    </html>
  );
}
