"use client";

import Link from "next/link";
import { EmptyState, PageHeader } from "@/components/ui";
import { Icons } from "@/components/ui/icons";
import { useI18n } from "@/lib/i18n/client";

/** Why the cases aren't shown, and what to do about it. */
export function AccessNotice({ problem }: { problem: string | null }) {
  const { t } = useI18n();
  const [title, description, action] =
    problem === "two_factor_required"
      ? [t.access.twoFactorTitle, t.access.twoFactorBody, <Link key="a" href="/settings?tab=account" className="text-sm font-medium text-brand-600 hover:underline">{t.access.goToSecurity}</Link>]
      : problem === "not_a_person"
        ? [t.access.notPersonTitle, t.access.notPersonBody, null]
        : [t.access.noRoleTitle, t.access.noRoleBody, null];
  return (
    <div>
      <PageHeader title={t.queue.title} />
      <div className="p-6">
        <EmptyState icon={<Icons.Lock className="h-5 w-5" />} title={title as string} description={description as string} action={action} />
      </div>
    </div>
  );
}
