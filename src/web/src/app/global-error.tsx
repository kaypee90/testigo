"use client";


/**
 * Shown when a page fails to render and nothing closer caught it. It replaces the root layout, so
 * it brings its own document and styles. Nothing is reported anywhere: errors could carry case
 * content (NFR-OPS-4).
 */
export default function GlobalError({ retry }: { error: unknown; retry: () => void }) {
  return (
    <html lang="en" style={{ colorScheme: "light dark" }}>
      <body style={{ fontFamily: "system-ui, sans-serif", margin: 0, minHeight: "100vh", display: "grid", placeItems: "center" }}>
        <title>Something went wrong</title>
        <main style={{ maxWidth: "28rem", padding: "1.5rem", textAlign: "center" }}>
          <h1 style={{ fontSize: "1.25rem", fontWeight: 600 }}>Something went wrong</h1>
          <p style={{ opacity: 0.75, lineHeight: 1.5 }}>The page hit an unexpected error. Try again, or reload the page if it keeps happening.</p>
          <button
            onClick={() => retry()}
            style={{ marginTop: "0.75rem", padding: "0.5rem 1rem", borderRadius: "0.375rem", border: "1px solid currentColor", background: "transparent", color: "inherit", font: "inherit", cursor: "pointer" }}
          >
            Try again
          </button>
        </main>
      </body>
    </html>
  );
}
