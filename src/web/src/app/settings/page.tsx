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
import { ROLES, ROLE_HELP, ROLE_LABELS, atLeast } from "@/lib/platformTypes";
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
          {copied ? "copiado" : "copiar"}
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
      <Section title="Organización">
        <div className="flex gap-2">
          <input value={name} aria-label="Nombre de la organización" onChange={(e) => setName(e.target.value)} disabled={!isAdmin} className={`${field} flex-1`} />
          {isAdmin && (
            <button className={button} disabled={!name.trim() || name === org?.name}
              onClick={() => act(async () => { await renameOrganization(name); await refresh(); })}>
              Cambiar nombre
            </button>
          )}
        </div>
        <div className="text-xs text-zinc-500">Id <code>{org?.tenant_id}</code> · tu rol: {ROLE_LABELS[role]}</div>
      </Section>

      <Section title={`Integrantes (${members.length})`}>
        <ul className="divide-y divide-zinc-200 text-sm dark:divide-zinc-800">
          {members.map((m) => {
            const self = m.user_id === me?.user?.user_id;
            return (
              <li key={m.user_id} className="flex items-center justify-between gap-2 py-2">
                <div className="min-w-0">
                  <div className="truncate font-medium">{m.name} {self && <span className="text-xs text-zinc-500">(tú)</span>}</div>
                  <div className="truncate text-xs text-zinc-500">{m.email}</div>
                </div>
                <div className="flex shrink-0 items-center gap-2">
                  {isAdmin && !self ? (
                    <select value={m.role} aria-label={`Rol de ${m.name}`} className={field} onChange={(e) => act(() => setMemberRole(m.user_id, e.target.value as Role))}>
                      {ROLES.map((r) => <option key={r} value={r}>{ROLE_LABELS[r]}</option>)}
                    </select>
                  ) : (
                    <span className="text-xs">{ROLE_LABELS[m.role]}</span>
                  )}
                  {(isAdmin || self) && (
                    <button className={subtle} onClick={() => {
                      if (confirm(self ? "¿Salir de esta organización?" : `¿Retirar a ${m.email}?`)) {
                        void act(async () => {
                          await removeMember(m.user_id);
                          // eslint-disable-next-line @next/next/no-location-assign-relative-destination -- a full reload drops everything loaded for the organization just left
                          if (self) window.location.assign("/login");
                        });
                      }
                    }}>
                      {self ? "Salir" : "Retirar"}
                    </button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      </Section>

      {isAdmin && (
        <Section title="Invitar a alguien">
          <form onSubmit={invite} className="flex flex-wrap gap-2">
            <input value={inviteEmail} onChange={(e) => setInviteEmail(e.target.value)} type="email" required placeholder="persona@ejemplo.invalid" aria-label="Correo de la persona invitada" className={`${field} min-w-0 flex-1`} />
            <select value={inviteRole} aria-label="Rol de la invitación" onChange={(e) => setInviteRole(e.target.value as Role)} className={field}>
              {ROLES.filter((r) => atLeast(role, r)).map((r) => <option key={r} value={r}>{ROLE_LABELS[r]}</option>)}
            </select>
            <button type="submit" className={button}>Crear invitación</button>
          </form>
          <p className="text-xs text-zinc-500">{ROLE_LABELS[inviteRole]}: {ROLE_HELP[inviteRole]}.</p>
          {link && <CopyOnce label="Envía este enlace (se muestra una sola vez y solo funciona con el correo de la persona invitada):" value={link} />}
          {invitations.filter((i) => !i.accepted_at && !i.revoked_at).length > 0 && (
            <ul className="space-y-1 text-xs">
              {invitations.filter((i) => !i.accepted_at && !i.revoked_at).map((i) => (
                <li key={i.invitation_id} className="flex items-center justify-between">
                  <span>{i.email} · {ROLE_LABELS[i.role]} · vence {new Date(i.expires_at).toLocaleDateString("es-CL")}</span>
                  <button className={subtle} onClick={() => act(() => revokeInvitation(i.invitation_id))}>Revocar</button>
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
    return <div className="text-sm text-zinc-500">Las cuentas están desactivadas en este servidor.</div>;
  }

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await changePassword(current, next);
      setMessage("Contraseña cambiada. Se cerraron las sesiones de los demás navegadores.");
      setCurrent("");
      setNext("");
    } catch (err) {
      setMessage(apiErrorMessage(err));
    }
  }

  return (
    <div className="space-y-4">
    <Section title={`Cuenta: ${me.user?.email ?? ""}`}>
      <form onSubmit={submit} className="flex max-w-sm flex-col gap-2">
        <input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} placeholder="Contraseña actual" aria-label="Contraseña actual" autoComplete="current-password" className={field} />
        <input type="password" value={next} onChange={(e) => setNext(e.target.value)} placeholder="Nueva contraseña (10 o más caracteres)" aria-label="Nueva contraseña" autoComplete="new-password" className={field} />
        <button type="submit" className={button} disabled={!current || !next}>Cambiar contraseña</button>
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
    ["organization", "Organización", true],
    ["account", "Cuenta", true],
  ];

  return (
    <div className="flex flex-col">
      <PageHeader title="Ajustes" description="Tu organización, sus integrantes y tu cuenta." />
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
