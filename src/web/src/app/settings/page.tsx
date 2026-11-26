"use client";

import { useSearchParams } from "next/navigation";
import { TwoFactorSettings } from "@/components/settings/TwoFactorSettings";
import { DeleteAccount } from "@/components/settings/DeleteAccount";
import { Suspense, useEffect, useState } from "react";
import {
  apiErrorMessage,
  changePassword,
  getOrganization,
  inviteMember,
  listInvitations,
  listMembers,
  removeMember,
  renameOrganization,
  revokeInvitation,
  setMemberRole,
} from "@/lib/api";
import { ROLES, ROLE_HELP, atLeast } from "@/lib/platformTypes";
import type { Invitation, Member, Organization, Role } from "@/lib/platformTypes";
import { PageHeader } from "@/components/ui";
import { useAuth } from "@/components/platform/AuthProvider";

type Tab = "organization" | "account";

const field = "rounded-lg border border-zinc-200 bg-white px-3 py-1.5 text-sm shadow-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-500/20 dark:border-zinc-800 dark:bg-zinc-950";
const button = "rounded-lg bg-brand-500 px-3.5 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-brand-600 disabled:opacity-50";
const subtle = "rounded-lg border border-zinc-200 px-2.5 py-1 text-xs hover:bg-zinc-100 dark:border-zinc-800 dark:hover:bg-zinc-800";

function Section({ title, children, right }: { title: string; children: React.ReactNode; right?: React.ReactNode }) {
  return (
    <section className="space-y-3 rounded-xl border border-zinc-200 bg-white p-5 shadow-sm dark:border-zinc-800 dark:bg-zinc-900/60">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold">{title}</h2>
        {right}
      </div>
      {children}
    </section>
  );
}

function CopyOnce({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="space-y-1 rounded border border-amber-300 bg-amber-50 p-2 text-xs dark:border-amber-800 dark:bg-amber-950/40">
      <div className="font-medium">{label}</div>
      <div className="flex gap-2">
        <code className="min-w-0 flex-1 truncate rounded bg-white px-1.5 py-1 dark:bg-zinc-900">{value}</code>
        <button type="button" className={subtle} onClick={() => navigator.clipboard.writeText(value).then(() => setCopied(true))}>
          {copied ? "copied" : "copy"}
        </button>
      </div>
    </div>
  );
}

function OrganizationTab({ role }: { role: Role }) {
  const { me, refresh } = useAuth();
  const [org, setOrg] = useState<Organization | null>(null);
  const [members, setMembers] = useState<Member[]>([]);
  const [invitations, setInvitations] = useState<Invitation[]>([]);
  const [name, setName] = useState("");
  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteRole, setInviteRole] = useState<Role>("Member");
  const [link, setLink] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const isAdmin = atLeast(role, "Admin");
  const [version, setVersion] = useState(0);

  useEffect(() => {
    let cancelled = false;
    Promise.all([getOrganization(), listMembers(), isAdmin ? listInvitations() : Promise.resolve([] as Invitation[])])
      .then(([o, m, i]) => {
        if (cancelled) return;
        setOrg(o);
        setName(o.name);
        setMembers(m);
        setInvitations(i);
      })
      .catch((err) => !cancelled && setError(apiErrorMessage(err)));
    return () => { cancelled = true; };
  }, [isAdmin, version]);

  async function act(fn: () => Promise<unknown>) {
    setError(null);
    try {
      await fn();
      setVersion((v) => v + 1);
    } catch (err) {
      setError(apiErrorMessage(err));
    }
  }

  async function invite(e: React.FormEvent) {
    e.preventDefault();
    await act(async () => {
      const created = await inviteMember(inviteEmail, inviteRole);
      setLink(`${window.location.origin}/login?invite=${encodeURIComponent(created.token)}`);
      setInviteEmail("");
    });
  }

  return (
    <div className="space-y-4">
      {error && <div className="text-sm text-rose-600">{error}</div>}
      <Section title="Organization">
        <div className="flex gap-2">
          <input value={name} onChange={(e) => setName(e.target.value)} disabled={!isAdmin} className={`${field} flex-1`} />
          {isAdmin && (
            <button className={button} disabled={!name.trim() || name === org?.name}
              onClick={() => act(async () => { await renameOrganization(name); await refresh(); })}>
              Rename
            </button>
          )}
        </div>
        <div className="text-xs text-zinc-500">Id <code>{org?.tenant_id}</code> · you are {role}</div>
      </Section>

      <Section title={`Members (${members.length})`}>
        <ul className="divide-y divide-zinc-200 text-sm dark:divide-zinc-800">
          {members.map((m) => {
            const self = m.user_id === me?.user?.user_id;
            return (
              <li key={m.user_id} className="flex items-center justify-between gap-2 py-2">
                <div className="min-w-0">
                  <div className="truncate font-medium">{m.name} {self && <span className="text-xs text-zinc-500">(you)</span>}</div>
                  <div className="truncate text-xs text-zinc-500">{m.email}</div>
                </div>
                <div className="flex shrink-0 items-center gap-2">
                  {isAdmin && !self ? (
                    <select value={m.role} className={field} onChange={(e) => act(() => setMemberRole(m.user_id, e.target.value as Role))}>
                      {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                    </select>
                  ) : (
                    <span className="text-xs">{m.role}</span>
                  )}
                  {(isAdmin || self) && (
                    <button className={subtle} onClick={() => {
                      if (confirm(self ? "Leave this organization?" : `Remove ${m.email}?`)) {
                        void act(async () => {
                          await removeMember(m.user_id);
                          // eslint-disable-next-line @next/next/no-location-assign-relative-destination -- a full reload drops everything loaded for the organization just left
                          if (self) window.location.assign("/login");
                        });
                      }
                    }}>
                      {self ? "Leave" : "Remove"}
                    </button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      </Section>

      {isAdmin && (
        <Section title="Invite someone">
          <form onSubmit={invite} className="flex flex-wrap gap-2">
            <input value={inviteEmail} onChange={(e) => setInviteEmail(e.target.value)} type="email" required placeholder="persona@ejemplo.org" className={`${field} min-w-0 flex-1`} />
            <select value={inviteRole} onChange={(e) => setInviteRole(e.target.value as Role)} className={field}>
              {ROLES.filter((r) => atLeast(role, r)).map((r) => <option key={r} value={r}>{r}</option>)}
            </select>
            <button type="submit" className={button}>Create invitation</button>
          </form>
          <p className="text-xs text-zinc-500">{inviteRole}: {ROLE_HELP[inviteRole]}.</p>
          {link && <CopyOnce label="Send them this link (shown once; it works for their email address only):" value={link} />}
          {invitations.filter((i) => !i.accepted_at && !i.revoked_at).length > 0 && (
            <ul className="space-y-1 text-xs">
              {invitations.filter((i) => !i.accepted_at && !i.revoked_at).map((i) => (
                <li key={i.invitation_id} className="flex items-center justify-between">
                  <span>{i.email} · {i.role} · expires {new Date(i.expires_at).toLocaleDateString()}</span>
                  <button className={subtle} onClick={() => act(() => revokeInvitation(i.invitation_id))}>Revoke</button>
                </li>
              ))}
            </ul>
          )}
        </Section>
      )}
    </div>
  );
}

function AccountTab() {
  const { me } = useAuth();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [message, setMessage] = useState<string | null>(null);

  if (me?.via !== "session") {
    return <div className="text-sm text-zinc-500">Accounts are disabled on this server.</div>;
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await changePassword(current, next);
      setMessage("Password changed. Other browsers were signed out.");
      setCurrent("");
      setNext("");
    } catch (err) {
      setMessage(apiErrorMessage(err));
    }
  }

  return (
    <div className="space-y-4">
    <Section title={`Account: ${me.user?.email ?? ""}`}>
      <form onSubmit={submit} className="flex max-w-sm flex-col gap-2">
        <input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} placeholder="Current password" autoComplete="current-password" className={field} />
        <input type="password" value={next} onChange={(e) => setNext(e.target.value)} placeholder="New password (10+ characters)" autoComplete="new-password" className={field} />
        <button type="submit" className={button} disabled={!current || !next}>Change password</button>
        {message && <div className="text-xs">{message}</div>}
      </form>
    </Section>
    <TwoFactorSettings />
    <DeleteAccount />
    </div>
  );
}

export default function SettingsPage() {
  return <Suspense><Settings /></Suspense>;
}

function Settings() {
  const { me } = useAuth();
  // ?tab=account picks the tab; useSearchParams stays current on in-app navigation.
  const params = useSearchParams();
  const urlTab = (params.get("tab") as Tab | null) ?? "organization";
  const [tab, setTab] = useState<Tab>(urlTab);
  const [seenUrlTab, setSeenUrlTab] = useState(urlTab);
  if (urlTab !== seenUrlTab) {
    setSeenUrlTab(urlTab);
    setTab(urlTab);
  }
  const role = (me?.role ?? "Viewer") as Role;

  const tabs: [Tab, string, boolean][] = [
    ["organization", "Organization", true],
    ["account", "Account", true],
  ];

  return (
    <div className="flex flex-col">
      <PageHeader title="Settings" description="Your organization, its members and your account." />
      <div className="mx-auto flex w-full max-w-5xl flex-1 gap-8 px-6 py-6">
        <aside className="w-40 shrink-0 space-y-1 text-sm">
          {tabs.filter(([, , visible]) => visible).map(([t, label]) => (
            <button key={t} onClick={() => setTab(t)}
              className={`block w-full rounded-lg px-3 py-2 text-left ${tab === t ? "bg-zinc-100 font-medium text-zinc-900 dark:bg-zinc-900 dark:text-zinc-50" : "text-zinc-600 hover:bg-zinc-50 dark:text-zinc-400 dark:hover:bg-zinc-900/60"}`}>
              {label}
            </button>
          ))}
        </aside>
        <main className="min-w-0 flex-1">
          {tab === "organization" && <OrganizationTab role={role} />}
          {tab === "account" && <AccountTab />}
        </main>
      </div>
    </div>
  );
}
