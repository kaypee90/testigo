import type { Metadata } from "next";

// Indexable pages are the landing page, the docs and early access; sign-in has nothing to find.
export const metadata: Metadata = {
  title: "Sign in",
  alternates: { canonical: "/login" },
  robots: { index: false, follow: true },
};

export default function LoginLayout({ children }: { children: React.ReactNode }) {
  return children;
}
