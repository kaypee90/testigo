import type { Metadata } from "next";
import "./globals.css";
import { AuthProvider } from "@/components/platform/AuthProvider";
import { AppShell } from "@/components/shell/AppShell";
import { PrototypeBanner } from "@/components/shell/PrototypeBanner";
import { ThemeScript } from "@/components/shell/theme";
import { DEFAULT_TITLE, SITE_DESCRIPTION, SITE_NAME } from "@/lib/site";

// No web fonts: the dashboard uses the system font stack, so no page asks a third party for
// anything (NFR-SEC-2), and the build doesn't need network access to a font service.
export const metadata: Metadata = {
  title: { default: DEFAULT_TITLE, template: `%s | ${SITE_NAME}` },
  description: SITE_DESCRIPTION,
  applicationName: SITE_NAME,
  // Staff pages hold case data: never indexed.
  robots: { index: false, follow: false },
  referrer: "no-referrer",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html
      lang="es"
      className="h-full antialiased"
      // The theme class is set by ThemeScript before React hydrates.
      suppressHydrationWarning
    >
      <head>
        <ThemeScript />
      </head>
      <body className="flex h-full flex-col bg-white text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
        {/* §12: every page carries the prototype banner while the project is in Phase 0. */}
        <PrototypeBanner />
        <AuthProvider>
          <AppShell>{children}</AppShell>
        </AuthProvider>
      </body>
    </html>
  );
}
