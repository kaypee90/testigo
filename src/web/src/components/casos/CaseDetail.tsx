"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Badge, Button, Card, CardHeader, ErrorBanner, PageHeader, cx, inputClass, inlineInputClass, type Tone } from "@/components/ui";
import { Icons } from "@/components/ui/icons";
import { getMe } from "@/lib/api";
import {
  addClassification, assignCase, caseAccessLog, changeStatus, decideLink, decideSuggestion, errorText, getCase, getTaxonomy, revealContact,
  type AccessEntry, type CaseView, type Suggestion, type Taxonomy, type TestigoMe,
} from "@/lib/testigo";
import { useI18n } from "@/lib/i18n/client";

const STATE_TONE: Record<string, Tone> = { suggested: "amber", confirmed: "green", rejected: "neutral" };

/**
 * One case (FR-CASE-2): the report, the AI's suggestions with the quote each one rests on
 * (FR-ANA-4), related reports, the consent record and the history. A person confirms, corrects or
 * rejects every suggestion (NFR-AI-3) and moves the case through its statuses with a reason
 * (FR-CASE-3). Opening the case is recorded in the audit log.
 */
export function CaseDetail({ caseId, me }: { caseId: string; me: TestigoMe }) {
  const { t, dateTime } = useI18n();
  const [view, setView] = useState<CaseView | null>(null);
  const [taxonomy, setTaxonomy] = useState<Taxonomy | null>(null);
  const [userId, setUserId] = useState<string | null>(null);
  // The raw error, worded at render time so it follows a language switch.
  const [error, setError] = useState<unknown>(null);
  const errorMessage = error === null ? null : errorText(error, t);
  const [busy, setBusy] = useState(false);
  const [highlight, setHighlight] = useState<Suggestion | null>(null);
  const [redacted, setRedacted] = useState(false);

  const load = useCallback(() => {
    getCase(caseId).then((v) => { setView(v); setError(null); }).catch(setError);
  }, [caseId]);

  useEffect(() => {
    load();
    getTaxonomy().then(setTaxonomy).catch(() => setTaxonomy(null));
    getMe().then((m) => setUserId(m.user?.user_id ?? null)).catch(() => setUserId(null));
  }, [load]);

  /** Runs one decision; `after` updates the view locally so a decision isn't another audited read. */
  const act = async (run: () => Promise<unknown>, after?: (v: CaseView) => CaseView) => {
    setBusy(true);
    setError(null);
    try {
      await run();
      if (after && view) setView(after(view));
      else load();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  if (!view) {
    return (
      <div>
        <PageHeader title={t.caseView.title} actions={<BackLink />} />
        <div className="p-6">{errorMessage ? <ErrorBanner error={errorMessage} /> : <p className="text-sm text-zinc-500">{t.common.loading}</p>}</div>
      </div>
    );
  }

  const s = view.summary;
  const setSuggestionState = (id: string, state: Suggestion["state"]) => (v: CaseView) => ({
    ...v,
    suggestions: v.suggestions.map((x) => (x.id === id ? { ...x, state, decided_by: userId, decided_at: new Date().toISOString() } : x)),
  });

  return (
    <div>
      <PageHeader
        title={<span className="flex flex-wrap items-center gap-2">{t.caseView.titleNumber(s.number)}
          <Badge tone="blue">{t.status[s.status] ?? s.status}</Badge>
          {s.immediate_danger && <Badge tone="red"><Icons.Alert className="h-3 w-3" />{t.queue.immediateDanger}</Badge>}
        </span>}
        description={t.caseView.description(dateTime(s.submitted_at), s.country, s.language, view.form_version, view.channel)}
        actions={<>
          <BackLink />
          {userId && s.assignee_id !== userId && (
            <Button size="sm" disabled={busy} onClick={() => act(() => assignCase(caseId, userId), (v) => ({ ...v, summary: { ...v.summary, assignee_id: userId } }))}>{t.caseView.assignMe}</Button>
          )}
          {s.assignee_id && (
            <Button size="sm" variant="ghost" disabled={busy} onClick={() => act(() => assignCase(caseId, null), (v) => ({ ...v, summary: { ...v.summary, assignee_id: null } }))}>
              {s.assignee_id === userId ? t.caseView.release : t.caseView.unassign}
            </Button>
          )}
        </>}
      />
      <div className="space-y-4 p-6">
        <ErrorBanner error={errorMessage} onClose={() => setError(null)} />

        {s.immediate_danger && (
          <div role="alert" className="rounded-lg border border-rose-300 bg-rose-50 px-4 py-3 text-sm text-rose-800 dark:border-rose-900 dark:bg-rose-950/40 dark:text-rose-200">
            <strong>{t.caseView.dangerTitle}</strong>{" "}
            {s.danger_source === "rules" ? t.caseView.dangerByRules : s.danger_source === "ai" ? t.caseView.dangerByAi : ""}{" "}
            {t.caseView.dangerAction}
          </div>
        )}
        {s.analysis_state !== "complete" && (
          <div className="rounded-lg border border-amber-300 bg-amber-50 px-4 py-2 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950/40 dark:text-amber-200">
            {t.analysis[s.analysis_state] ?? s.analysis_state}
            {s.analysis_state === "incomplete" && t.caseView.incompleteNote}
            {view.analysis_note && <span className="text-amber-700 dark:text-amber-300"> ({view.analysis_note})</span>}
          </div>
        )}

        <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
          <div className="space-y-4">
            <Card>
              <CardHeader
                title={t.caseView.report}
                description={redacted ? t.caseView.redactedDescription(view.redaction_method ?? "—") : t.caseView.originalDescription}
                actions={view.redacted_text !== null && (
                  <div className="flex gap-1" role="group" aria-label={t.caseView.textVersion}>
                    <Button size="sm" variant={redacted ? "ghost" : "secondary"} onClick={() => setRedacted(false)}>{t.caseView.original}</Button>
                    <Button size="sm" variant={redacted ? "secondary" : "ghost"} onClick={() => setRedacted(true)}>{t.caseView.analysisCopy}</Button>
                  </div>
                )}
              />
              <div className="px-5 py-4">
                <ReportText text={redacted ? view.redacted_text ?? "" : view.text} mark={redacted ? null : highlight} />
                {view.redaction_types.length > 0 && (
                  <p className="mt-3 text-xs text-zinc-500">{t.caseView.replaced(view.redaction_types.map((k) => t.redaction[k] ?? k).join(", "))}</p>
                )}
                {s.language !== "es" && <p className="mt-2 text-xs text-zinc-500">{t.caseView.translationLater}</p>}
              </div>
            </Card>

            <Card>
              <CardHeader title={t.caseView.answers} />
              <dl className="divide-y divide-zinc-100 px-5 text-sm dark:divide-zinc-800">
                {view.answers.map((a) => (
                  <div key={a.field} className="grid grid-cols-3 gap-3 py-2">
                    <dt className="text-zinc-500">{a.label}</dt>
                    <dd className="col-span-2">{a.values.join(", ") || "—"}</dd>
                  </div>
                ))}
              </dl>
            </Card>

            <ConsentCard view={view} caseId={caseId} />
          </div>

          <div className="space-y-4">
            <SuggestionsCard
              view={view}
              taxonomy={taxonomy}
              busy={busy}
              highlight={highlight}
              onHighlight={(x) => { setRedacted(false); setHighlight(highlight?.id === x.id ? null : x); }}
              onConfirm={(x) => act(() => decideSuggestion(caseId, x.id, "confirm"), setSuggestionState(x.id, "confirmed"))}
              onReject={(x) => act(() => decideSuggestion(caseId, x.id, "reject"), setSuggestionState(x.id, "rejected"))}
              onEdit={(x, term, value) => act(() => decideSuggestion(caseId, x.id, "edit", term, value))}
              onAdd={(dimension, term) => act(() => addClassification(caseId, dimension, term))}
            />
            <LinksCard
              view={view}
              busy={busy}
              onDecide={(id, action) => act(() => decideLink(caseId, id, action), (v) => ({
                ...v,
                links: v.links.map((l) => (l.id === id ? { ...l, state: action === "confirm" ? "confirmed" : "rejected", decided_by: userId } : l)),
              }))}
            />
            <StatusCard view={view} busy={busy} reviewer={me.roles.includes("reviewer")} onChange={(to, reason) => act(() => changeStatus(caseId, to, reason))} />
            <HistoryCard view={view} />
            {me.roles.includes("reviewer") && <AccessLogCard caseId={caseId} />}
          </div>
        </div>
      </div>
    </div>
  );
}

function BackLink() {
  const { t } = useI18n();
  return (
    <Link href="/casos" className="inline-flex h-7 items-center gap-1 rounded-lg px-2.5 text-xs font-medium text-zinc-600 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800">
      <Icons.ChevronLeft className="h-3.5 w-3.5" />{t.caseView.backToQueue}
    </Link>
  );
}

/** The report text, with the quote of the selected suggestion marked. Plain text only: never HTML. */
function ReportText({ text, mark }: { text: string; mark: Suggestion | null }) {
  const start = mark?.start ?? null;
  const length = mark?.length ?? null;
  const ok = start !== null && length !== null && start >= 0 && start + length <= text.length;
  return (
    <div className="whitespace-pre-wrap break-words text-sm leading-relaxed text-zinc-800 dark:text-zinc-200">
      {ok ? (
        <>
          {text.slice(0, start)}
          <mark className="rounded bg-amber-200 px-0.5 text-zinc-900 dark:bg-amber-600/60 dark:text-zinc-50">{text.slice(start, start + length)}</mark>
          {text.slice(start + length)}
        </>
      ) : text}
    </div>
  );
}

function SuggestionsCard({ view, taxonomy, busy, highlight, onHighlight, onConfirm, onReject, onEdit, onAdd }: {
  view: CaseView;
  taxonomy: Taxonomy | null;
  busy: boolean;
  highlight: Suggestion | null;
  onHighlight: (s: Suggestion) => void;
  onConfirm: (s: Suggestion) => void;
  onReject: (s: Suggestion) => void;
  onEdit: (s: Suggestion, term?: string, value?: string) => void;
  onAdd: (dimension: string, term: string) => void;
}) {
  const { t } = useI18n();
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [addDimension, setAddDimension] = useState("");
  const [addTerm, setAddTerm] = useState("");
  const dims = useMemo(() => new Map(taxonomy?.dimensions.map((d) => [d.key, d]) ?? []), [taxonomy]);
  const groups: [string, Suggestion[]][] = [
    [t.suggestions.groupDanger, view.suggestions.filter((x) => x.kind === "danger")],
    [t.suggestions.groupTerms, view.suggestions.filter((x) => x.kind === "term")],
    [t.suggestions.groupFields, view.suggestions.filter((x) => x.kind === "field")],
  ];

  return (
    <Card>
      <CardHeader title={t.suggestions.title} description={t.suggestions.description} />
      <div className="divide-y divide-zinc-100 dark:divide-zinc-800">
        {view.suggestions.length === 0 && <p className="px-5 py-4 text-sm text-zinc-500">{t.suggestions.none}</p>}
        {groups.filter(([, xs]) => xs.length > 0).map(([title, xs]) => (
          <section key={title} className="px-5 py-3">
            <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-zinc-500">{title}</h4>
            <ul className="space-y-3">
              {xs.map((x) => {
                const dim = dims.get(x.dimension);
                return (
                  <li key={x.id} className={cx("rounded-lg border p-3", highlight?.id === x.id ? "border-amber-400" : "border-zinc-200 dark:border-zinc-800")}>
                    <div className="flex flex-wrap items-center gap-2 text-sm">
                      <span className="text-zinc-500">{dim?.label ?? x.dimension}:</span>
                      <span className="font-medium">{x.kind === "field" ? x.value : x.label ?? x.term ?? (x.kind === "danger" ? t.common.yes : "—")}</span>
                      <Badge tone={STATE_TONE[x.state]}>{t.decision[x.state] ?? x.state}</Badge>
                      <span className="text-xs text-zinc-500">{t.source[x.source] ?? x.source}{x.model ? ` · ${x.model}` : ""}{x.prompt_version ? ` · ${x.prompt_version}` : ""}</span>
                    </div>
                    {x.quote && (
                      <button type="button" onClick={() => onHighlight(x)} className="mt-2 block w-full rounded border-l-4 border-amber-300 bg-amber-50/60 px-3 py-1.5 text-left text-sm italic text-zinc-700 hover:bg-amber-100/70 dark:bg-amber-950/20 dark:text-zinc-300">
                        «{x.quote}»
                      </button>
                    )}
                    {editing === x.id ? (
                      <div className="mt-2 flex flex-wrap items-center gap-2">
                        {x.kind === "term" ? (
                          <select aria-label={t.suggestions.correctedTerm} className={inlineInputClass} value={draft} onChange={(e) => setDraft(e.target.value)}>
                            <option value="">{t.suggestions.pickTerm}</option>
                            {dim?.terms.map((term) => <option key={term.key} value={term.key}>{term.label}</option>)}
                          </select>
                        ) : (
                          <input aria-label={t.suggestions.correctedValue} className={inlineInputClass} value={draft} onChange={(e) => setDraft(e.target.value)} maxLength={300} />
                        )}
                        <Button size="sm" variant="primary" disabled={busy || !draft.trim()} onClick={() => { onEdit(x, x.kind === "term" ? draft : undefined, x.kind === "field" ? draft : undefined); setEditing(null); }}>{t.common.save}</Button>
                        <Button size="sm" variant="ghost" onClick={() => setEditing(null)}>{t.common.cancel}</Button>
                      </div>
                    ) : (
                      <div className="mt-2 flex flex-wrap gap-2">
                        {x.state !== "confirmed" && <Button size="sm" disabled={busy} onClick={() => onConfirm(x)} icon={<Icons.Check className="h-3.5 w-3.5" />}>{t.common.confirm}</Button>}
                        {x.kind !== "danger" && <Button size="sm" disabled={busy} onClick={() => { setEditing(x.id); setDraft(x.kind === "term" ? x.term ?? "" : x.value ?? ""); }}>{t.suggestions.correct}</Button>}
                        {x.state !== "rejected" && <Button size="sm" variant="ghost" disabled={busy} onClick={() => onReject(x)} icon={<Icons.X className="h-3.5 w-3.5" />}>{t.common.discard}</Button>}
                      </div>
                    )}
                  </li>
                );
              })}
            </ul>
          </section>
        ))}
        {taxonomy && (
          <form
            className="flex flex-wrap items-end gap-2 px-5 py-3"
            onSubmit={(e) => { e.preventDefault(); if (addDimension && addTerm) { onAdd(addDimension, addTerm); setAddTerm(""); } }}
          >
            <label className="text-xs text-zinc-500">
              {t.suggestions.addClassification}
              <select className={cx(inlineInputClass, "mt-1 block")} value={addDimension} onChange={(e) => { setAddDimension(e.target.value); setAddTerm(""); }}>
                <option value="">{t.suggestions.dimension}</option>
                {taxonomy.dimensions.map((d) => <option key={d.key} value={d.key}>{d.label}</option>)}
              </select>
            </label>
            <select aria-label={t.suggestions.term} className={inlineInputClass} value={addTerm} onChange={(e) => setAddTerm(e.target.value)} disabled={!addDimension}>
              <option value="">{t.suggestions.term}</option>
              {dims.get(addDimension)?.terms.map((term) => <option key={term.key} value={term.key} title={term.description ?? undefined}>{term.label}</option>)}
            </select>
            <Button size="sm" type="submit" disabled={busy || !addDimension || !addTerm} icon={<Icons.Plus className="h-3.5 w-3.5" />}>{t.suggestions.add}</Button>
            <span className="w-full text-xs text-zinc-500">{t.suggestions.taxonomy(taxonomy.version, taxonomy.status)}</span>
          </form>
        )}
      </div>
    </Card>
  );
}

function LinksCard({ view, busy, onDecide }: { view: CaseView; busy: boolean; onDecide: (id: string, action: "confirm" | "reject") => void }) {
  const { t } = useI18n();
  return (
    <Card>
      <CardHeader title={t.related.title} description={t.related.description} />
      <ul className="divide-y divide-zinc-100 px-5 text-sm dark:divide-zinc-800">
        {view.links.length === 0 && <li className="py-3 text-zinc-500">{t.related.none}</li>}
        {view.links.map((l) => (
          <li key={l.id} className="flex flex-wrap items-center gap-2 py-2">
            <Link href={`/casos?id=${l.other_case_id}`} className="font-medium text-brand-600 hover:underline">{l.other_number}</Link>
            <span>{t.links[l.kind] ?? l.kind}</span>
            <span className="text-xs text-zinc-500">{t.related.similarity(l.score.toFixed(2))} · {t.status[l.other_status] ?? l.other_status}</span>
            <Badge tone={STATE_TONE[l.state]}>{t.decision[l.state] ?? l.state}</Badge>
            <span className="ml-auto flex gap-2">
              {l.state !== "confirmed" && <Button size="sm" disabled={busy} onClick={() => onDecide(l.id, "confirm")}>{t.common.confirm}</Button>}
              {l.state !== "rejected" && <Button size="sm" variant="ghost" disabled={busy} onClick={() => onDecide(l.id, "reject")}>{t.common.discard}</Button>}
            </span>
          </li>
        ))}
      </ul>
    </Card>
  );
}

function StatusCard({ view, busy, reviewer, onChange }: { view: CaseView; busy: boolean; reviewer: boolean; onChange: (to: string, reason: string) => void }) {
  const { t, dateTime } = useI18n();
  const [to, setTo] = useState("");
  const [reason, setReason] = useState("");
  const confirmedTerms = view.suggestions.filter((x) => x.kind === "term" && x.state === "confirmed").length;
  const verifying = to === "verified";
  return (
    <Card>
      <CardHeader title={t.statusCard.title} description={t.statusCard.description} />
      <form
        className="space-y-3 px-5 py-4"
        onSubmit={(e) => { e.preventDefault(); if (to && reason.trim()) { onChange(to, reason.trim()); setTo(""); setReason(""); } }}
      >
        {view.allowed_statuses.length === 0 ? (
          <p className="text-sm text-zinc-500">{t.statusCard.final}</p>
        ) : (
          <>
            <label className="block text-xs text-zinc-500">
              {t.statusCard.moveTo}
              <select className={cx(inputClass, "mt-1")} value={to} onChange={(e) => setTo(e.target.value)}>
                <option value="">{t.statusCard.pick}</option>
                {view.allowed_statuses.map((x) => <option key={x} value={x}>{t.status[x] ?? x}</option>)}
              </select>
            </label>
            {verifying && (
              <p className={cx("text-xs", reviewer && confirmedTerms > 0 ? "text-zinc-500" : "text-amber-700 dark:text-amber-300")}>
                {!reviewer ? t.statusCard.needsReviewer : confirmedTerms === 0 ? t.statusCard.needsConfirmed : t.statusCard.evidence(confirmedTerms)}
              </p>
            )}
            <label className="block text-xs text-zinc-500">
              {t.statusCard.reason}
              <textarea className={cx(inputClass, "mt-1")} rows={2} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
            </label>
            <Button type="submit" variant="primary" size="sm" disabled={busy || !to || !reason.trim()}>{t.statusCard.change}</Button>
          </>
        )}
        {view.verified_at && <p className="text-xs text-zinc-500">{t.statusCard.reviewed(dateTime(view.verified_at))}</p>}
      </form>
    </Card>
  );
}

function ConsentCard({ view, caseId }: { view: CaseView; caseId: string }) {
  const { t, dateTime } = useI18n();
  const c = view.consent;
  const [reason, setReason] = useState("");
  const [contact, setContact] = useState<{ method: string; value: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const yes = (b: boolean) => (b ? t.common.yes : t.common.no);
  return (
    <Card>
      <CardHeader title={t.consent.title} description={t.consent.description(c.notice_version, dateTime(c.recorded_at))} />
      <div className="space-y-3 px-5 py-4 text-sm">
        <dl className="grid grid-cols-2 gap-x-4 gap-y-1">
          <dt className="text-zinc-500">{t.consent.processing}</dt><dd>{yes(c.processing)}</dd>
          <dt className="text-zinc-500">{t.consent.contact}</dt><dd>{yes(c.contact)}</dd>
          <dt className="text-zinc-500">{t.consent.publication}</dt><dd>{yes(c.publication)}</dd>
          {c.withdrawn_at && <><dt className="text-zinc-500">{t.consent.withdrawn}</dt><dd>{dateTime(c.withdrawn_at)}</dd></>}
        </dl>
        {c.has_contact_details && c.contact && (
          contact ? (
            <p className="rounded-lg bg-zinc-50 px-3 py-2 dark:bg-zinc-900"><span className="text-zinc-500">{contact.method}:</span> {contact.value}</p>
          ) : (
            <form className="space-y-2" onSubmit={(e) => {
              e.preventDefault();
              setError(null);
              revealContact(caseId, reason.trim()).then(setContact).catch((err) => setError(errorText(err, t)));
            }}>
              <label className="block text-xs text-zinc-500">
                {t.consent.revealLabel}
                <select className={cx(inputClass, "mt-1")} value={reason} onChange={(e) => setReason(e.target.value)}>
                  <option value="">{t.consent.pickReason}</option>
                  <option value="follow_up">{t.consent.reasonFollowUp}</option>
                  <option value="clarification">{t.consent.reasonClarification}</option>
                  <option value="support_referral">{t.consent.reasonSupport}</option>
                </select>
              </label>
              <Button size="sm" type="submit" disabled={!reason.trim()} icon={<Icons.Eye className="h-3.5 w-3.5" />}>{t.consent.reveal}</Button>
              <ErrorBanner error={error} />
            </form>
          )
        )}
      </div>
    </Card>
  );
}

function HistoryCard({ view }: { view: CaseView }) {
  const { t, dateTime } = useI18n();
  return (
    <Card>
      <CardHeader title={t.history.title} />
      <ol className="divide-y divide-zinc-100 px-5 text-sm dark:divide-zinc-800">
        {view.history.length === 0 && <li className="py-3 text-zinc-500">{t.history.none}</li>}
        {view.history.map((h, i) => (
          <li key={i} className="py-2">
            <div>{t.status[h.from] ?? h.from} → <strong>{t.status[h.to] ?? h.to}</strong> <span className="text-xs text-zinc-500">{dateTime(h.at)} · {h.by_user_id}</span></div>
            <div className="text-zinc-600 dark:text-zinc-400">{h.reason}</div>
          </li>
        ))}
      </ol>
    </Card>
  );
}

function AccessLogCard({ caseId }: { caseId: string }) {
  const { t, dateTime } = useI18n();
  const [items, setItems] = useState<AccessEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  return (
    <Card>
      <CardHeader
        title={t.accessLog.title}
        description={t.accessLog.description}
        actions={<Button size="sm" onClick={() => caseAccessLog(caseId).then((r) => setItems(r.items)).catch((e) => setError(errorText(e, t)))}>{items ? t.common.refresh : t.common.show}</Button>}
      />
      <div className="px-5 py-3 text-sm">
        <ErrorBanner error={error} />
        {items && (
          <ul className="divide-y divide-zinc-100 dark:divide-zinc-800">
            {items.map((a, i) => (
              <li key={i} className="py-1.5">
                <span className="text-xs text-zinc-500">{dateTime(a.at)}</span> · {a.actor_name || a.actor_id} · {a.summary}
                {a.outcome !== "success" && <Badge tone="red" className="ml-2">{a.outcome}</Badge>}
              </li>
            ))}
          </ul>
        )}
      </div>
    </Card>
  );
}
