import type { Metadata } from "next";
import { requestMessages } from "@/lib/i18n/server";

// A one-time link from an email: nothing here for search engines.
export async function generateMetadata(): Promise<Metadata> {
  const t = await requestMessages();
  return {
    title: t.meta.confirmEmail,
    robots: { index: false, follow: false },
  };
}

export default function VerifyEmailLayout({ children }: { children: React.ReactNode }) {
  return children;
}
