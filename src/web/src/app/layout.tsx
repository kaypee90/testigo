import type { Metadata } from "next";
import "./globals.css";
import { AuthProvider } from "@/components/platform/AuthProvider";
import { AppShell } from "@/components/shell/AppShell";
import { PrototypeBanner } from "@/components/shell/PrototypeBanner";
import { ThemeScript } from "@/components/shell/theme";
import { LanguageProvider } from "@/lib/i18n/client";
import { requestLang, requestMessages } from "@/lib/i18n/server";
import { SITE_NAME } from "@/lib/site";

// No web fonts: the dashboard uses the system font stack, so no page asks a third party for
// anything (NFR-SEC-2), and the build doesn't need network access to a font service.
export async function generateMetadata(): Promise<Metadata> {
  const t = await requestMessages();
  return {
    title: { default: t.meta.title, template: `%s | ${SITE_NAME}` },
    description: t.meta.description,
    applicationName: SITE_NAME,
    // Staff pages hold case data: never indexed.
    robots: { index: false, follow: false },
    referrer: "no-referrer",
  };
}

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  // The language comes from its cookie, so the first render is already in it.
  const lang = await requestLang();
  return (
    <html
      lang={lang}
      className="h-full antialiased"
      // The theme class is set by ThemeScript before React hydrates.
      suppressHydrationWarning
    >
      <head>
        <ThemeScript />
      </head>
      <body className="flex h-full flex-col bg-white text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
        <LanguageProvider initial={lang}>
          {/* §12: every page carries the prototype banner while the project is in Phase 0. */}
          <PrototypeBanner />
          <AuthProvider>
            <AppShell>{children}</AppShell>
          </AuthProvider>
        </LanguageProvider>
      </body>
    </html>
  );
}
