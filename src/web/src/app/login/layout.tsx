import type { Metadata } from "next";
import { requestMessages } from "@/lib/i18n/server";

// Indexable pages are the landing page, the docs and early access; sign-in has nothing to find.
export async function generateMetadata(): Promise<Metadata> {
  const t = await requestMessages();
  return {
    title: t.meta.signIn,
    alternates: { canonical: "/login" },
    robots: { index: false, follow: true },
  };
}

export default function LoginLayout({ children }: { children: React.ReactNode }) {
  return children;
}
