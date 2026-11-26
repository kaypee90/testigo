"use client";

import { useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { CaseQueue } from "@/components/casos/CaseQueue";
import { CaseDetail } from "@/components/casos/CaseDetail";
import { AccessNotice } from "@/components/casos/AccessNotice";
import { errorText, testigoMe, type TestigoMe } from "@/lib/testigo";
import { ErrorBanner } from "@/components/ui";
import { useI18n } from "@/lib/i18n/client";

/**
 * Testigo cases (FR-CASE-1, FR-CASE-2): /casos is the queue, /casos?id=<case id> one case.
 * Only people with the Analyst or Reviewer role and two-factor sign-in reach them; the API checks
 * that on every request, this page only explains a refusal.
 */
export default function CasosPage() {
  return (
    <Suspense>
      <CasosFromUrl />
    </Suspense>
  );
}

function CasosFromUrl() {
  const params = useSearchParams();
  const { t } = useI18n();
  const id = params.get("id");
  const [me, setMe] = useState<TestigoMe | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    testigoMe().then(setMe).catch(setError);
  }, []);

  if (error) return <div className="p-6"><ErrorBanner error={errorText(error, t)} /></div>;
  if (!me) return <div className="p-6 text-sm text-zinc-500">{t.common.loading}</div>;
  if (!me.allowed) return <AccessNotice problem={me.problem} />;
  return id ? <CaseDetail key={id} caseId={id} me={me} /> : <CaseQueue />;
}
