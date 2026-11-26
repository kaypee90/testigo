"use client";

import { useCallback, useEffect, useState } from "react";
import {
  adminAddMember,
  adminRemoveMember,
  adminResetTwoFactor,
  adminRevokeInvitation,
  adminSetMemberRole,
  apiErrorMessage,
  createAdminOrganization,
  getAdminOrganization,
  getAdminOverview,
  listAdminOrganizations,
  reactivateOrganization,
  suspendOrganization,
  updateAdminOrganization,
} from "@/lib/api";
import { ROLES } from "@/lib/platformTypes";
import type { AddedPerson, AdminOrganization, AdminOrganizationDetail, AdminOverview, Role } from "@/lib/platformTypes";
import { useAuth } from "@/components/platform/AuthProvider";
import { Icons } from "@/components/ui/icons";
import {
  Badge,
  Button,
  Card,
  CardHeader,
  CopyField,
  EmptyState,
  ErrorBanner,
  Field,
  Modal,
  PageHeader,
  Stat,
  Tabs,
  inlineInputClass,
  inputClass,
} from "@/components/ui";
import { useI18n } from "@/lib/i18n/client";

type Tab = "overview" | "organizations";

/**
 * The platform admin portal (docs/platform.md#platform-administration): every organization on the
 * server, the people in each, and suspension. Operators only; the API
 * enforces that, this page just doesn't offer anything to anyone else.
 */
export default function AdminPage() {
  const { me } = useAuth();
  const { t } = useI18n();
  const [tab, setTab] = useState<Tab>("overview");
  const [overview, setOverview] = useState<AdminOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [openOrg, setOpenOrg] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const isOperator = !!me?.user?.platform_admin;

  const refreshOverview = useCallback(() => {
    getAdminOverview().then(setOverview).catch((e) => setError(apiErrorMessage(e)));
  }, []);

  useEffect(() => {
    if (isOperator) refreshOverview();
  }, [isOperator, refreshOverview]);

  if (!isOperator) {
    return (
      <div className="p-6">
        <EmptyState icon={<Icons.Lock className="h-5 w-5" />} title={t.admin.operatorsOnly} description={t.admin.operatorsOnlyBody} />
      </div>
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col overflow-y-auto">
      <PageHeader
        title={t.admin.title}
        description={t.admin.description}
        actions={<Button variant="primary" icon={<Icons.Plus className="h-4 w-4" />} onClick={() => setCreating(true)}>{t.admin.newOrganization}</Button>}
      >
        <Tabs<Tab>
          className="mt-4 -mb-5"
          value={tab}
          onChange={setTab}
          tabs={[
            { id: "overview", label: t.admin.tabOverview },
            { id: "organizations", label: t.admin.tabOrganizations, count: overview?.organizations },
          ]}
        />
      </PageHeader>

      <div className="mx-auto w-full max-w-6xl space-y-4 p-4 sm:p-6">
        <ErrorBanner error={error} onClose={() => setError(null)} />
        {overview && !overview.email_enabled && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:border-amber-900/60 dark:bg-amber-950/40 dark:text-amber-300">
            {t.admin.noEmail}
          </div>
        )}

        {tab === "overview" && <OverviewTab overview={overview} />}
        {tab === "organizations" && <OrganizationsTab onOpen={setOpenOrg} onError={setError} />}
      </div>

      {openOrg && <OrganizationModal tenantId={openOrg} onClose={() => { setOpenOrg(null); refreshOverview(); }} />}
      {creating && (
        <CreateOrganizationModal onClose={() => setCreating(false)}
          onCreated={(id) => { refreshOverview(); setTab("organizations"); setOpenOrg(id); }} />
      )}
    </div>
  );
}

// ---- Overview ---------------------------------------------------------------------------

function OverviewTab({ overview }: { overview: AdminOverview | null }) {
  const { t, ago } = useI18n();
  if (!overview) return <div className="text-sm text-zinc-500">{t.common.loading}</div>;
  return (
    <>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3">
        <Stat label={t.admin.statOrganizations} value={overview.organizations} hint={t.admin.statActive(overview.active_organizations)} />
        <Stat label={t.admin.statSuspended} value={overview.suspended_organizations} />
        <Stat label={t.admin.statMembers} value={overview.members} hint={t.admin.statMembersHint} />
      </div>
      <Card>
        <CardHeader title={t.admin.activityTitle} description={t.admin.activityDescription} />
        {overview.recent_activity.length === 0 ? (
          <div className="px-5 py-6 text-sm text-zinc-500">{t.admin.nothingYet}</div>
        ) : (
          <ul className="divide-y divide-zinc-100 dark:divide-zinc-800">
            {overview.recent_activity.map((a, i) => (
              <li key={i} className="flex flex-wrap items-baseline gap-x-3 gap-y-1 px-5 py-2.5 text-sm">
                <span className="w-20 shrink-0 text-xs text-zinc-500">{ago(a.at)}</span>
                <span className="min-w-0 flex-1">{a.summary}</span>
                <span className="text-xs text-zinc-500">{a.actor}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
      <VersionCard version={overview.version} />
      <ResetTwoFactorCard />
    </>
  );
}

/** The commit this dashboard was built from, baked in at build time; empty in development. */
const WEB_VERSION = process.env.NEXT_PUBLIC_APP_VERSION || null;

/** Which build is running: the server's commit and when it was built and started, and whether the
 * website was built from the same commit (a deploy that only half finished shows here). */
function VersionCard({ version }: { version: AdminOverview["version"] }) {
  const server = version?.commit ?? null;
  const { t, dateTime, ago } = useI18n();
  const differs = !!server && !!WEB_VERSION && server !== WEB_VERSION;
  const when = (at: string | null | undefined) => (at ? `${dateTime(at)} (${ago(at)})` : t.admin.notRecorded);
  return (
    <Card>
      <CardHeader title={t.admin.versionTitle} description={t.admin.versionDescription} />
      <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 px-5 pb-5 text-sm">
        <dt className="text-zinc-500">{t.admin.server}</dt>
        <dd className="font-mono">
          {!server ? <span className="font-sans text-zinc-500">{t.admin.notStamped}</span>
            : version?.short_commit}
        </dd>
        <dt className="text-zinc-500">{t.admin.built}</dt>
        <dd>{when(version?.built_at)}</dd>
        <dt className="text-zinc-500">{t.admin.runningSince}</dt>
        <dd>{when(version?.started_at)}</dd>
        <dt className="text-zinc-500">{t.admin.website}</dt>
        <dd className="font-mono">
          {WEB_VERSION ? WEB_VERSION.slice(0, 7) : <span className="font-sans text-zinc-500">{t.admin.notStamped}</span>}
        </dd>
      </dl>
      {differs && (
        <div className="mx-5 mb-5 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-200">
          {t.admin.versionsDiffer}
        </div>
      )}
    </Card>
  );
}

/** For someone who lost their phone and their recovery codes: they sign in with their password
 * again, then turn two-factor sign-in back on themselves. */
function ResetTwoFactorCard() {
  const { t } = useI18n();
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  async function reset(e: React.FormEvent) {
    e.preventDefault();
    const who = email.trim();
    if (!confirm(t.admin.confirmReset(who))) return;
    setBusy(true);
    setMessage(null);
    try {
      await adminResetTwoFactor(who);
      setMessage({ ok: true, text: t.admin.resetDone(who) });
      setEmail("");
    } catch (err) {
      setMessage({ ok: false, text: apiErrorMessage(err) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader title={t.admin.lockedTitle} description={t.admin.lockedDescription} />
      <form onSubmit={reset} className="flex flex-wrap items-center gap-2 px-5 pb-5">
        {/* Text, not email: the local admin signs in with a username. */}
        <input type="text" value={email} onChange={(e) => setEmail(e.target.value)} placeholder={t.admin.lockedPlaceholder} required
          aria-label={t.admin.lockedLabel} autoCapitalize="none" spellCheck={false} className={`${inputClass} max-w-xs`} />
        <Button size="sm" type="submit" disabled={busy || !email.trim()}>{busy ? t.common.busy : t.admin.turnOff}</Button>
        {message && <span className={`text-xs ${message.ok ? "text-emerald-700 dark:text-emerald-400" : "text-rose-600"}`}>{message.text}</span>}
      </form>
    </Card>
  );
}

// ---- Organizations ----------------------------------------------------------------------------

function OrgStatus({ org }: { org: Pick<AdminOrganization, "status"> }) {
  const { t } = useI18n();
  return org.status === "Suspended" ? <Badge tone="red">{t.admin.suspended}</Badge> : <Badge tone="green">{t.admin.active}</Badge>;
}

function OrganizationsTab({ onOpen, onError }: { onOpen: (id: string) => void; onError: (e: string) => void }) {
  const { t, date, ago } = useI18n();
  const [orgs, setOrgs] = useState<AdminOrganization[] | null>(null);
  const [q, setQ] = useState("");
  const [status, setStatus] = useState("");

  useEffect(() => {
    const t = setTimeout(() => {
      listAdminOrganizations(q || undefined, status || undefined).then(setOrgs).catch((e) => onError(apiErrorMessage(e)));
    }, 200);
    return () => clearTimeout(t);
  }, [q, status, onError]);

  return (
    <Card>
      <div className="flex flex-wrap gap-2 border-b border-zinc-200 px-4 py-3 dark:border-zinc-800">
        <input value={q} onChange={(e) => setQ(e.target.value)} placeholder={t.admin.search} aria-label={t.admin.search} className={`${inlineInputClass} min-w-0 flex-1`} />
        <select value={status} onChange={(e) => setStatus(e.target.value)} className={inlineInputClass} aria-label={t.admin.status}>
          <option value="">{t.admin.allStatuses}</option>
          <option value="Active">{t.admin.active}</option>
          <option value="Suspended">{t.admin.suspended}</option>
        </select>
      </div>
      {orgs === null ? (
        <div className="px-5 py-6 text-sm text-zinc-500">{t.common.loading}</div>
      ) : orgs.length === 0 ? (
        <div className="p-5"><EmptyState title={t.admin.noMatch} /></div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-[11px] uppercase tracking-wide text-zinc-500">
              <tr className="border-b border-zinc-200 dark:border-zinc-800">
                <th className="px-4 py-2 font-medium">{t.admin.colOrganization}</th>
                <th className="px-4 py-2 font-medium">{t.admin.colStatus}</th>
                <th className="px-4 py-2 font-medium">{t.admin.colMembers}</th>
                <th className="hidden px-4 py-2 font-medium md:table-cell">{t.admin.colOwner}</th>
                <th className="hidden px-4 py-2 font-medium md:table-cell">{t.admin.colLastActive}</th>
                <th className="hidden px-4 py-2 font-medium lg:table-cell">{t.admin.colCreated}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-zinc-100 dark:divide-zinc-800">
              {orgs.map((o) => (
                <tr key={o.tenant_id} onClick={() => onOpen(o.tenant_id)} className="cursor-pointer hover:bg-zinc-50 dark:hover:bg-zinc-900">
                  <td className="px-4 py-2.5">
                    <div className="font-medium">{o.name}</div>
                    <div className="font-mono text-[11px] text-zinc-500">{o.tenant_id}</div>
                  </td>
                  <td className="px-4 py-2.5"><OrgStatus org={o} /></td>
                  <td className="px-4 py-2.5 tabular-nums">
                    {o.members}{o.pending_invitations > 0 && <span className="text-xs text-zinc-500">{t.admin.invitedCount(o.pending_invitations)}</span>}
                  </td>
                  <td className="hidden px-4 py-2.5 md:table-cell">{o.owner_email ?? "—"}</td>
                  <td className="hidden px-4 py-2.5 text-zinc-500 md:table-cell">{ago(o.last_active_at)}</td>
                  <td className="hidden px-4 py-2.5 text-zinc-500 lg:table-cell">{date(o.created_at)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

/** What adding someone did, with the link to pass on when it wasn't emailed. */
function AddedNotice({ person }: { person: AddedPerson }) {
  const { t } = useI18n();
  const role = t.roles.labels[person.role] ?? person.role;
  if (person.joined) {
    return (
      <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-800 dark:border-emerald-900/60 dark:bg-emerald-950/40 dark:text-emerald-300">
        {t.admin.joined(person.email, role)}{person.email_sent ? t.admin.emailed : ""}
      </div>
    );
  }

  return (
    <div className="space-y-2 rounded-lg border border-sky-200 bg-sky-50 p-3 text-xs text-sky-900 dark:border-sky-900/60 dark:bg-sky-950/40 dark:text-sky-200">
      <div>
        {t.admin.invitedAs(person.email, role)}{" "}
        {person.email_sent ? t.admin.emailedLink : t.admin.shareLink}
      </div>
      {person.invite_url && <CopyField value={person.invite_url} />}
    </div>
  );
}

function CreateOrganizationModal({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const { t } = useI18n();
  const [name, setName] = useState("");
  const [owner, setOwner] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<{ id: string; owner: AddedPerson } | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const created = await createAdminOrganization({ name, owner_email: owner });
      setResult({ id: created.tenant_id, owner: created.owner });
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal open onClose={onClose} title={t.admin.newOrganization} description={t.admin.createDescription}
      footer={result
        ? <Button variant="primary" onClick={() => { onClose(); onCreated(result.id); }}>{t.admin.openOrganization}</Button>
        : <><Button variant="ghost" onClick={onClose}>{t.common.cancel}</Button><Button variant="primary" type="submit" form="create-org" disabled={busy}>{busy ? t.admin.creating : t.admin.create}</Button></>}>
      {result ? (
        <AddedNotice person={result.owner} />
      ) : (
        <form id="create-org" onSubmit={submit} className="space-y-3">
          <Field label={t.admin.name}><input value={name} onChange={(e) => setName(e.target.value)} required maxLength={100} className={inputClass} placeholder={t.admin.namePlaceholder} /></Field>
          <Field label={t.admin.ownerEmail} hint={t.admin.ownerHint}>
            <input value={owner} onChange={(e) => setOwner(e.target.value)} type="email" required className={inputClass} placeholder={t.admin.emailPlaceholder} />
          </Field>
          <ErrorBanner error={error} />
        </form>
      )}
    </Modal>
  );
}

function OrganizationModal({ tenantId, onClose }: { tenantId: string; onClose: () => void }) {
  const { me } = useAuth();
  const { t, date, ago } = useI18n();
  const [detail, setDetail] = useState<AdminOrganizationDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("Member");
  const [added, setAdded] = useState<AddedPerson | null>(null);
  const [name, setName] = useState("");
  const [suspending, setSuspending] = useState(false);
  const [reason, setReason] = useState("");

  const load = useCallback(() => {
    getAdminOrganization(tenantId)
      .then((d) => { setDetail(d); setName(d.organization.name); })
      .catch((e) => setError(apiErrorMessage(e)));
  }, [tenantId]);

  useEffect(load, [load]);

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      load();
    } catch (err) {
      setError(apiErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  const org = detail?.organization;
  const isOwnOrg = me?.organizations?.some((o) => o.tenant_id === tenantId);

  return (
    <Modal open wide onClose={onClose} title={org?.name ?? t.admin.organization} description={org ? `${org.tenant_id} · ${t.admin.created(date(org.created_at))}` : undefined}>
      {!detail || !org ? (
        error ? <ErrorBanner error={error} /> : <div className="text-sm text-zinc-500">{t.common.loading}</div>
      ) : (
        <div className="space-y-5">
          <ErrorBanner error={error} onClose={() => setError(null)} />

          <div className="flex flex-wrap items-center gap-2">
            <OrgStatus org={org} />
            {org.status === "Suspended" && <span className="text-xs text-zinc-500">{t.admin.since(date(org.suspended_at), org.suspended_reason)}</span>}
            <div className="ml-auto flex gap-2">
              {org.status === "Suspended" ? (
                <Button size="sm" variant="primary" disabled={busy} onClick={() => run(() => reactivateOrganization(tenantId))}>{t.admin.reactivate}</Button>
              ) : (
                <Button size="sm" variant="danger" disabled={busy || isOwnOrg}
                  title={isOwnOrg ? t.admin.ownOrg : undefined}
                  onClick={() => setSuspending(true)}>{t.admin.suspend}</Button>
              )}
            </div>
          </div>

          {suspending && (
            <div className="space-y-2 rounded-lg border border-rose-200 bg-rose-50 p-3 dark:border-rose-900/60 dark:bg-rose-950/30">
              <div className="text-sm font-medium text-rose-800 dark:text-rose-300">{t.admin.suspendQuestion(org.name)}</div>
              <p className="text-xs text-rose-700 dark:text-rose-300/80">
                {t.admin.suspendBody}
              </p>
              <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder={t.admin.suspendReason} aria-label={t.admin.suspendReason} className={inputClass} />
              <div className="flex justify-end gap-2">
                <Button size="sm" variant="ghost" onClick={() => setSuspending(false)}>{t.common.cancel}</Button>
                <Button size="sm" variant="danger" disabled={busy} onClick={() => run(async () => { await suspendOrganization(tenantId, reason); setSuspending(false); setReason(""); })}>
                  {t.admin.suspendOrganization}
                </Button>
              </div>
            </div>
          )}

          <div className="grid gap-3 sm:grid-cols-2">
            <Field label={t.admin.name}>
              <div className="flex gap-2">
                <input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} className={inputClass} />
                <Button disabled={busy || !name.trim() || name === org.name} onClick={() => run(() => updateAdminOrganization(tenantId, { name }))}>{t.common.save}</Button>
              </div>
            </Field>
          </div>

          <div className="space-y-2">
            <h3 className="text-sm font-semibold">{t.admin.people}</h3>
            <form className="flex flex-wrap gap-2" onSubmit={(e) => {
              e.preventDefault();
              void run(async () => { setAdded(await adminAddMember(tenantId, email, role)); setEmail(""); });
            }}>
              <input value={email} onChange={(e) => setEmail(e.target.value)} type="email" required placeholder={t.admin.emailPlaceholder} aria-label={t.admin.emailPlaceholder} className={`${inlineInputClass} min-w-0 flex-1`} />
              <select value={role} onChange={(e) => setRole(e.target.value as Role)} className={inlineInputClass} aria-label={t.admin.role}>
                {ROLES.map((r) => <option key={r} value={r}>{t.roles.labels[r]}</option>)}
              </select>
              <Button type="submit" variant="primary" disabled={busy}>{t.admin.addPerson}</Button>
            </form>
            {added && <AddedNotice person={added} />}

            <div className="overflow-x-auto rounded-lg border border-zinc-200 dark:border-zinc-800">
              <table className="w-full text-sm">
                <tbody className="divide-y divide-zinc-100 dark:divide-zinc-800">
                  {detail.members.map((m) => (
                    <tr key={m.user_id}>
                      <td className="px-3 py-2">
                        <div className="font-medium">{m.name}</div>
                        <div className="text-xs text-zinc-500">{m.email}</div>
                      </td>
                      <td className="hidden px-3 py-2 text-xs text-zinc-500 sm:table-cell">{t.admin.lastSignIn(ago(m.last_login_at))}</td>
                      <td className="px-3 py-2">
                        <select value={m.role} disabled={busy} aria-label={t.admin.roleOf(m.email)}
                          onChange={(e) => run(() => adminSetMemberRole(tenantId, m.user_id, e.target.value as Role))} className={inlineInputClass}>
                          {ROLES.map((r) => <option key={r} value={r}>{t.roles.labels[r]}</option>)}
                        </select>
                      </td>
                      <td className="px-3 py-2 text-right">
                        <Button size="sm" variant="ghost" disabled={busy} title={t.admin.removeTitle} aria-label={t.admin.removeTitle}
                          onClick={() => { if (confirm(t.admin.confirmRemove(m.email, org.name))) void run(() => adminRemoveMember(tenantId, m.user_id)); }}>
                          <Icons.Trash className="h-3.5 w-3.5" />
                        </Button>
                      </td>
                    </tr>
                  ))}
                  {detail.invitations.filter((i) => !i.accepted_at && !i.revoked_at && new Date(i.expires_at) > new Date()).map((i) => (
                    <tr key={i.invitation_id} className="bg-zinc-50/60 dark:bg-zinc-900/40">
                      <td className="px-3 py-2">
                        <div className="text-zinc-600 dark:text-zinc-400">{i.email}</div>
                        <div className="text-xs text-zinc-500">{t.admin.invitedExpires(date(i.expires_at))}</div>
                      </td>
                      <td className="hidden sm:table-cell" />
                      <td className="px-3 py-2"><Badge tone="amber">{t.roles.labels[i.role] ?? i.role}</Badge></td>
                      <td className="px-3 py-2 text-right">
                        <Button size="sm" variant="ghost" disabled={busy} onClick={() => run(() => adminRevokeInvitation(tenantId, i.invitation_id))}>{t.admin.revoke}</Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}
    </Modal>
  );
}
