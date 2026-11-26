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
  ago,
  inlineInputClass,
  inputClass,
} from "@/components/ui";

type Tab = "overview" | "organizations";

const date = (iso: string | null | undefined) => (iso ? new Date(iso).toLocaleDateString() : "—");

/**
 * The platform admin portal (docs/platform.md#platform-administration): every organization on the
 * server, the people in each, and suspension. Operators only; the API
 * enforces that, this page just doesn't offer anything to anyone else.
 */
export default function AdminPage() {
  const { me } = useAuth();
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
        <EmptyState icon={<Icons.Lock className="h-5 w-5" />} title="Operators only"
          description="The platform admin portal is for the server's operators (PLATFORM_ADMIN_EMAIL)." />
      </div>
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col overflow-y-auto">
      <PageHeader
        title="Platform admin"
        description="Every organization on this server and the people in them."
        actions={<Button variant="primary" icon={<Icons.Plus className="h-4 w-4" />} onClick={() => setCreating(true)}>New organization</Button>}
      >
        <Tabs<Tab>
          className="mt-4 -mb-5"
          value={tab}
          onChange={setTab}
          tabs={[
            { id: "overview", label: "Overview" },
            { id: "organizations", label: "Organizations", count: overview?.organizations },
          ]}
        />
      </PageHeader>

      <div className="mx-auto w-full max-w-6xl space-y-4 p-4 sm:p-6">
        <ErrorBanner error={error} onClose={() => setError(null)} />
        {overview && !overview.email_enabled && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:border-amber-900/60 dark:bg-amber-950/40 dark:text-amber-300">
            Outgoing email isn&apos;t set up (EMAIL_SMTP_HOST), so invitations aren&apos;t emailed: copy each invite link and send it yourself.
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
  if (!overview) return <div className="text-sm text-zinc-500">Loading…</div>;
  return (
    <>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3">
        <Stat label="Organizations" value={overview.organizations} hint={`${overview.active_organizations} active`} />
        <Stat label="Suspended" value={overview.suspended_organizations} />
        <Stat label="Members" value={overview.members} hint="across all organizations" />
      </div>
      <Card>
        <CardHeader title="Recent operator activity" description="Every change made here is recorded in the platform audit log." />
        {overview.recent_activity.length === 0 ? (
          <div className="px-5 py-6 text-sm text-zinc-500">Nothing yet.</div>
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
  const differs = !!server && !!WEB_VERSION && server !== WEB_VERSION;
  const when = (at: string | null | undefined) => (at ? `${new Date(at).toLocaleString()} (${ago(at)})` : "not recorded");
  return (
    <Card>
      <CardHeader title="Version" description="Which build is running. Each deploy is stamped with the commit it was built from." />
      <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 px-5 pb-5 text-sm">
        <dt className="text-zinc-500">Server</dt>
        <dd className="font-mono">
          {!server ? <span className="font-sans text-zinc-500">Not stamped (a local or manual build)</span>
            : version?.short_commit}
        </dd>
        <dt className="text-zinc-500">Built</dt>
        <dd>{when(version?.built_at)}</dd>
        <dt className="text-zinc-500">Running since</dt>
        <dd>{when(version?.started_at)}</dd>
        <dt className="text-zinc-500">Website</dt>
        <dd className="font-mono">
          {WEB_VERSION ? WEB_VERSION.slice(0, 7) : <span className="font-sans text-zinc-500">Not stamped (a local or manual build)</span>}
        </dd>
      </dl>
      {differs && (
        <div className="mx-5 mb-5 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-200">
          The website and the server were built from different commits: a deploy may not have finished.
        </div>
      )}
    </Card>
  );
}

/** For someone who lost their phone and their recovery codes: they sign in with their password
 * again, then turn two-factor sign-in back on themselves. */
function ResetTwoFactorCard() {
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  async function reset(e: React.FormEvent) {
    e.preventDefault();
    const who = email.trim();
    if (!confirm(`Turn off two-factor sign-in for ${who} and sign out all their browsers? Only do this once you're sure it's really them asking.`)) return;
    setBusy(true);
    setMessage(null);
    try {
      await adminResetTwoFactor(who);
      setMessage({ ok: true, text: `Two-factor sign-in is off for ${who}. They can sign in with their password and turn it back on.` });
      setEmail("");
    } catch (err) {
      setMessage({ ok: false, text: apiErrorMessage(err) });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader title="Locked out of two-factor sign-in"
        description="For someone who lost both their phone and their recovery codes. Check it's really them first, e.g. by replying from the email address on the account." />
      <form onSubmit={reset} className="flex flex-wrap items-center gap-2 px-5 pb-5">
        {/* Text, not email: the local admin signs in with a username. */}
        <input type="text" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="Their email (or username)" required
          aria-label="Email or username of the locked-out account" autoCapitalize="none" spellCheck={false} className={`${inputClass} max-w-xs`} />
        <Button size="sm" type="submit" disabled={busy || !email.trim()}>{busy ? "…" : "Turn off two-factor"}</Button>
        {message && <span className={`text-xs ${message.ok ? "text-emerald-700 dark:text-emerald-400" : "text-rose-600"}`}>{message.text}</span>}
      </form>
    </Card>
  );
}

// ---- Organizations ----------------------------------------------------------------------------

function OrgStatus({ org }: { org: Pick<AdminOrganization, "status"> }) {
  return org.status === "Suspended" ? <Badge tone="red">Suspended</Badge> : <Badge tone="green">Active</Badge>;
}

function OrganizationsTab({ onOpen, onError }: { onOpen: (id: string) => void; onError: (e: string) => void }) {
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
        <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search by name, id or member email" className={`${inlineInputClass} min-w-0 flex-1`} />
        <select value={status} onChange={(e) => setStatus(e.target.value)} className={inlineInputClass} aria-label="Status">
          <option value="">All statuses</option>
          <option value="Active">Active</option>
          <option value="Suspended">Suspended</option>
        </select>
      </div>
      {orgs === null ? (
        <div className="px-5 py-6 text-sm text-zinc-500">Loading…</div>
      ) : orgs.length === 0 ? (
        <div className="p-5"><EmptyState title="No organizations match" /></div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-[11px] uppercase tracking-wide text-zinc-500">
              <tr className="border-b border-zinc-200 dark:border-zinc-800">
                <th className="px-4 py-2 font-medium">Organization</th>
                <th className="px-4 py-2 font-medium">Status</th>
                <th className="px-4 py-2 font-medium">Members</th>
                <th className="hidden px-4 py-2 font-medium md:table-cell">Owner</th>
                <th className="hidden px-4 py-2 font-medium md:table-cell">Last active</th>
                <th className="hidden px-4 py-2 font-medium lg:table-cell">Created</th>
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
                    {o.members}{o.pending_invitations > 0 && <span className="text-xs text-zinc-500"> +{o.pending_invitations} invited</span>}
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
  if (person.joined) {
    return (
      <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-800 dark:border-emerald-900/60 dark:bg-emerald-950/40 dark:text-emerald-300">
        {person.email} already had an account and now has {person.role} access.{person.email_sent ? " We emailed them." : ""}
      </div>
    );
  }

  return (
    <div className="space-y-2 rounded-lg border border-sky-200 bg-sky-50 p-3 text-xs text-sky-900 dark:border-sky-900/60 dark:bg-sky-950/40 dark:text-sky-200">
      <div>
        Invited {person.email} as {person.role}.{" "}
        {person.email_sent ? "We emailed them the link." : "Email isn't set up or didn't send, so share this one-time link with them:"}
      </div>
      {person.invite_url && <CopyField value={person.invite_url} />}
    </div>
  );
}

function CreateOrganizationModal({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
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
    <Modal open onClose={onClose} title="New organization" description="It gets its own members and cases. Its owner sets their own password from the invitation."
      footer={result
        ? <Button variant="primary" onClick={() => { onClose(); onCreated(result.id); }}>Open organization</Button>
        : <><Button variant="ghost" onClick={onClose}>Cancel</Button><Button variant="primary" type="submit" form="create-org" disabled={busy}>{busy ? "Creating…" : "Create"}</Button></>}>
      {result ? (
        <AddedNotice person={result.owner} />
      ) : (
        <form id="create-org" onSubmit={submit} className="space-y-3">
          <Field label="Name"><input value={name} onChange={(e) => setName(e.target.value)} required maxLength={100} className={inputClass} placeholder="Organización Ejemplo" /></Field>
          <Field label="Owner's email" hint="An existing account joins straight away; anyone else gets an invitation.">
            <input value={owner} onChange={(e) => setOwner(e.target.value)} type="email" required className={inputClass} placeholder="persona@ejemplo.org" />
          </Field>
          <ErrorBanner error={error} />
        </form>
      )}
    </Modal>
  );
}

function OrganizationModal({ tenantId, onClose }: { tenantId: string; onClose: () => void }) {
  const { me } = useAuth();
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
    <Modal open wide onClose={onClose} title={org?.name ?? "Organization"} description={org ? `${org.tenant_id} · created ${date(org.created_at)}` : undefined}>
      {!detail || !org ? (
        error ? <ErrorBanner error={error} /> : <div className="text-sm text-zinc-500">Loading…</div>
      ) : (
        <div className="space-y-5">
          <ErrorBanner error={error} onClose={() => setError(null)} />

          <div className="flex flex-wrap items-center gap-2">
            <OrgStatus org={org} />
            {org.status === "Suspended" && <span className="text-xs text-zinc-500">since {date(org.suspended_at)}: {org.suspended_reason}</span>}
            <div className="ml-auto flex gap-2">
              {org.status === "Suspended" ? (
                <Button size="sm" variant="primary" disabled={busy} onClick={() => run(() => reactivateOrganization(tenantId))}>Reactivate</Button>
              ) : (
                <Button size="sm" variant="danger" disabled={busy || isOwnOrg}
                  title={isOwnOrg ? "You belong to this organization; suspending it would lock you out." : undefined}
                  onClick={() => setSuspending(true)}>Suspend</Button>
              )}
            </div>
          </div>

          {suspending && (
            <div className="space-y-2 rounded-lg border border-rose-200 bg-rose-50 p-3 dark:border-rose-900/60 dark:bg-rose-950/30">
              <div className="text-sm font-medium text-rose-800 dark:text-rose-300">Suspend {org.name}?</div>
              <p className="text-xs text-rose-700 dark:text-rose-300/80">
                Members are signed out and can&apos;t sign in to it, and its invitations stop working. Nothing is deleted;
                reactivating restores everything.
              </p>
              <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Reason (shown to operators)" className={inputClass} />
              <div className="flex justify-end gap-2">
                <Button size="sm" variant="ghost" onClick={() => setSuspending(false)}>Cancel</Button>
                <Button size="sm" variant="danger" disabled={busy} onClick={() => run(async () => { await suspendOrganization(tenantId, reason); setSuspending(false); setReason(""); })}>
                  Suspend organization
                </Button>
              </div>
            </div>
          )}

          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Name">
              <div className="flex gap-2">
                <input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} className={inputClass} />
                <Button disabled={busy || !name.trim() || name === org.name} onClick={() => run(() => updateAdminOrganization(tenantId, { name }))}>Save</Button>
              </div>
            </Field>
          </div>

          <div className="space-y-2">
            <h3 className="text-sm font-semibold">People</h3>
            <form className="flex flex-wrap gap-2" onSubmit={(e) => {
              e.preventDefault();
              void run(async () => { setAdded(await adminAddMember(tenantId, email, role)); setEmail(""); });
            }}>
              <input value={email} onChange={(e) => setEmail(e.target.value)} type="email" required placeholder="persona@ejemplo.org" className={`${inlineInputClass} min-w-0 flex-1`} />
              <select value={role} onChange={(e) => setRole(e.target.value as Role)} className={inlineInputClass} aria-label="Role">
                {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
              </select>
              <Button type="submit" variant="primary" disabled={busy}>Add person</Button>
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
                      <td className="hidden px-3 py-2 text-xs text-zinc-500 sm:table-cell">last sign-in {ago(m.last_login_at)}</td>
                      <td className="px-3 py-2">
                        <select value={m.role} disabled={busy} aria-label={`Role of ${m.email}`}
                          onChange={(e) => run(() => adminSetMemberRole(tenantId, m.user_id, e.target.value as Role))} className={inlineInputClass}>
                          {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                        </select>
                      </td>
                      <td className="px-3 py-2 text-right">
                        <Button size="sm" variant="ghost" disabled={busy} title="Remove from this organization"
                          onClick={() => { if (confirm(`Remove ${m.email} from ${org.name}?`)) void run(() => adminRemoveMember(tenantId, m.user_id)); }}>
                          <Icons.Trash className="h-3.5 w-3.5" />
                        </Button>
                      </td>
                    </tr>
                  ))}
                  {detail.invitations.filter((i) => !i.accepted_at && !i.revoked_at && new Date(i.expires_at) > new Date()).map((i) => (
                    <tr key={i.invitation_id} className="bg-zinc-50/60 dark:bg-zinc-900/40">
                      <td className="px-3 py-2">
                        <div className="text-zinc-600 dark:text-zinc-400">{i.email}</div>
                        <div className="text-xs text-zinc-500">invited · expires {date(i.expires_at)}</div>
                      </td>
                      <td className="hidden sm:table-cell" />
                      <td className="px-3 py-2"><Badge tone="amber">{i.role}</Badge></td>
                      <td className="px-3 py-2 text-right">
                        <Button size="sm" variant="ghost" disabled={busy} onClick={() => run(() => adminRevokeInvitation(tenantId, i.invitation_id))}>Revoke</Button>
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
