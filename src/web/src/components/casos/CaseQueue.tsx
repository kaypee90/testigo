"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { Badge, Button, EmptyState, ErrorBanner, PageHeader, cx, inlineInputClass } from "@/components/ui";
import { Icons } from "@/components/ui/icons";
import { errorText, getTaxonomy, listCases, type CaseSummary, type QueueFilter, type Taxonomy } from "@/lib/testigo";
import { useI18n } from "@/lib/i18n/client";

const PAGE = 50;

/**
 * The case queue (FR-CASE-1): new and assigned cases, the ones in immediate danger first, then the
 * oldest. Rows carry no report content: opening a case is a separate, audited read.
 */
export function CaseQueue() {
  const { t, dateTime } = useI18n();
  const [filter, setFilter] = useState<QueueFilter>({});
  const [offset, setOffset] = useState(0);
  const [items, setItems] = useState<CaseSummary[] | null>(null);
  const [total, setTotal] = useState(0);
  const [taxonomy, setTaxonomy] = useState<Taxonomy | null>(null);
  // The raw error, worded at render time so it follows a language switch.
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    getTaxonomy().then(setTaxonomy).catch(() => setTaxonomy(null));
  }, []);

  const load = useCallback(() => {
    listCases({ ...filter, limit: PAGE, offset })
      .then((page) => {
        setItems(page.items);
        setTotal(page.total);
        setError(null);
      })
      .catch(setError);
  }, [filter, offset]);

  useEffect(load, [load]);

  const set = (patch: Partial<QueueFilter>) => {
    setOffset(0);
    setFilter((f) => ({ ...f, ...patch }));
  };

  return (
    <div>
      <PageHeader
        title={t.queue.title}
        description={t.queue.description}
        actions={<Button size="sm" onClick={load} icon={<Icons.Replay className="h-3.5 w-3.5" />}>{t.common.refresh}</Button>}
      />
      <div className="space-y-4 p-6">
        <div className="flex flex-wrap items-end gap-3" role="search" aria-label={t.queue.filters}>
          <label className="text-xs text-zinc-500">
            {t.queue.status}
            <select className={cx(inlineInputClass, "mt-1 block")} value={filter.status ?? ""} onChange={(e) => set({ status: e.target.value || undefined })}>
              <option value="">{t.queue.all}</option>
              {Object.entries(t.status).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
            </select>
          </label>
          <label className="text-xs text-zinc-500">
            {t.queue.term}
            <select className={cx(inlineInputClass, "mt-1 block max-w-64")} value={filter.term ?? ""} onChange={(e) => set({ term: e.target.value || undefined })}>
              <option value="">{t.queue.all}</option>
              {taxonomy?.dimensions.map((d) => (
                <optgroup key={d.key} label={d.label}>
                  {d.terms.map((term) => <option key={`${d.key}:${term.key}`} value={`${d.key}:${term.key}`}>{term.label}</option>)}
                </optgroup>
              ))}
            </select>
          </label>
          <label className="text-xs text-zinc-500">
            {t.queue.country}
            <input className={cx(inlineInputClass, "mt-1 block w-20 uppercase")} maxLength={2} placeholder="CL" value={filter.country ?? ""}
              onChange={(e) => set({ country: e.target.value.toUpperCase() || undefined })} />
          </label>
          <label className="text-xs text-zinc-500">
            {t.queue.assignment}
            <select className={cx(inlineInputClass, "mt-1 block")} value={filter.assignee ?? ""} onChange={(e) => set({ assignee: e.target.value || undefined })}>
              <option value="">{t.queue.all}</option>
              <option value="me">{t.queue.assignedToMe}</option>
              <option value="none">{t.queue.unassigned}</option>
            </select>
          </label>
          <label className="text-xs text-zinc-500">
            {t.queue.from}
            <input type="date" className={cx(inlineInputClass, "mt-1 block")} value={filter.from?.slice(0, 10) ?? ""}
              onChange={(e) => set({ from: e.target.value ? `${e.target.value}T00:00:00Z` : undefined })} />
          </label>
          <label className="text-xs text-zinc-500">
            {t.queue.to}
            <input type="date" className={cx(inlineInputClass, "mt-1 block")} value={filter.to?.slice(0, 10) ?? ""}
              onChange={(e) => set({ to: e.target.value ? `${e.target.value}T23:59:59Z` : undefined })} />
          </label>
          <label className="flex items-center gap-2 pb-2 text-sm text-zinc-700 dark:text-zinc-300">
            <input type="checkbox" checked={!!filter.danger} onChange={(e) => set({ danger: e.target.checked })} />
            {t.queue.dangerOnly}
          </label>
        </div>

        <ErrorBanner error={error === null ? null : errorText(error, t)} />

        {items === null ? (
          <p className="text-sm text-zinc-500">{t.common.loading}</p>
        ) : items.length === 0 ? (
          <EmptyState icon={<Icons.File className="h-5 w-5" />} title={t.queue.none} />
        ) : (
          <div className="overflow-x-auto rounded-xl border border-zinc-200 dark:border-zinc-800">
            <table className="w-full text-left text-sm">
              <thead className="bg-zinc-50 text-xs uppercase tracking-wide text-zinc-500 dark:bg-zinc-900">
                <tr>
                  <th className="px-4 py-2 font-medium">{t.queue.colCase}</th>
                  <th className="px-4 py-2 font-medium">{t.queue.colStatus}</th>
                  <th className="px-4 py-2 font-medium">{t.queue.colTerms}</th>
                  <th className="px-4 py-2 font-medium">{t.queue.colCountry}</th>
                  <th className="px-4 py-2 font-medium">{t.queue.colReceived}</th>
                  <th className="px-4 py-2 font-medium">{t.queue.colPending}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-200 dark:divide-zinc-800">
                {items.map((c) => (
                  <tr key={c.id} className={cx("hover:bg-zinc-50 dark:hover:bg-zinc-900/60", c.immediate_danger && "bg-rose-50/60 dark:bg-rose-950/20")}>
                    <td className="px-4 py-2">
                      <Link href={`/casos?id=${c.id}`} className="font-medium text-brand-600 hover:underline">{c.number}</Link>
                      {c.immediate_danger && <Badge tone="red" className="ml-2"><Icons.Alert className="h-3 w-3" />{t.queue.immediateDanger}</Badge>}
                    </td>
                    <td className="px-4 py-2">
                      <div>{t.status[c.status] ?? c.status}</div>
                      <div className={cx("text-xs", c.analysis_state === "incomplete" ? "text-amber-600" : "text-zinc-500")}>{t.analysis[c.analysis_state] ?? c.analysis_state}</div>
                    </td>
                    <td className="px-4 py-2">
                      <div className="flex flex-wrap gap-1">
                        {c.terms.slice(0, 4).map((term) => (
                          <Badge key={`${term.dimension}:${term.term}`} tone={term.state === "confirmed" ? "green" : "neutral"}>{term.label}</Badge>
                        ))}
                        {c.terms.length > 4 && <span className="text-xs text-zinc-500">+{c.terms.length - 4}</span>}
                      </div>
                    </td>
                    <td className="px-4 py-2">{c.country ?? "—"}</td>
                    <td className="px-4 py-2 whitespace-nowrap">{dateTime(c.submitted_at)}</td>
                    <td className="px-4 py-2 tabular-nums">{c.pending_suggestions}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {total > PAGE && (
          <div className="flex items-center justify-between text-sm text-zinc-500">
            <span>{t.queue.range(offset + 1, Math.min(offset + PAGE, total), total)}</span>
            <div className="flex gap-2">
              <Button size="sm" disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - PAGE))}>{t.queue.previous}</Button>
              <Button size="sm" disabled={offset + PAGE >= total} onClick={() => setOffset(offset + PAGE)}>{t.queue.next}</Button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
