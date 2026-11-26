"use client";

import { useEffect, useState } from "react";
import { apiErrorMessage, deleteAccount, getAccountDeletion, type AccountDeletionPreview, type DeletionOrg } from "@/lib/api";
import { useI18n } from "@/lib/i18n/client";

const field = "rounded-lg border border-zinc-300 bg-white px-3 py-1.5 text-sm dark:border-zinc-700 dark:bg-zinc-900";

/**
 * Settings → Account → Delete account (docs/platform.md#deleting-your-account). Shows exactly what
 * happens to each organization first: the ones left, the ones closed and erased with the account,
 * and the ones that need another owner before it can go. Asks for the password and a typed phrase.
 */
export function DeleteAccount() {
  const { t } = useI18n();
  // The phrase to type, in the page's language; checked here only, as a guard against a slip.
  const CONFIRM = t.deleteAccount.phrase;
  const [preview, setPreview] = useState<AccountDeletionPreview | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [password, setPassword] = useState("");
  const [typed, setTyped] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    let stopped = false;
    getAccountDeletion()
      .then((p) => !stopped && setPreview(p))
      .catch((err) => !stopped && setLoadError(apiErrorMessage(err)));
    return () => { stopped = true; };
  }, [open]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await deleteAccount(password);
      // eslint-disable-next-line @next/next/no-location-assign-relative-destination -- a full reload drops everything loaded while signed in
      window.location.assign("/login");
    } catch (err) {
      setError(apiErrorMessage(err));
      setBusy(false);
    }
  }

  return (
    <section className="space-y-3 rounded-xl border border-rose-200 bg-white p-5 shadow-sm dark:border-rose-900/60 dark:bg-zinc-900/60">
      <div>
        <h2 className="text-sm font-semibold text-rose-700 dark:text-rose-400">{t.deleteAccount.title}</h2>
        <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
          {t.deleteAccount.intro}
        </p>
      </div>

      {!open ? (
        <button onClick={() => setOpen(true)}
          className="rounded-lg border border-rose-300 px-3.5 py-1.5 text-sm font-medium text-rose-700 hover:bg-rose-50 dark:border-rose-800 dark:text-rose-400 dark:hover:bg-rose-950/40">
          {t.deleteAccount.open}
        </button>
      ) : loadError ? (
        <p className="text-sm text-rose-600">{loadError}</p>
      ) : !preview ? (
        <p className="text-sm text-zinc-500">{t.deleteAccount.checking}</p>
      ) : (
        <div className="space-y-3">
          {preview.is_operator && (
            <p className="text-sm text-amber-700 dark:text-amber-400">
              {t.deleteAccount.operator}
            </p>
          )}
          <OrgList title={t.deleteAccount.blocksTitle} tone="amber" orgs={preview.blocks} note={t.deleteAccount.blocksNote} />
          <OrgList title={t.deleteAccount.closesTitle} tone="rose" orgs={preview.closes} note={t.deleteAccount.closesNote} />
          <OrgList title={t.deleteAccount.leavesTitle} tone="zinc" orgs={preview.leaves} note={t.deleteAccount.leavesNote} />

          {preview.allowed && (
            <form onSubmit={submit} className="flex max-w-sm flex-col gap-2">
              <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder={t.deleteAccount.yourPassword} aria-label={t.deleteAccount.yourPassword}
                autoComplete="current-password" className={field} />
              <label className="flex flex-col gap-1 text-xs text-zinc-500">
                <span>{t.deleteAccount.typeBefore} <span className="font-semibold text-zinc-700 dark:text-zinc-300">{CONFIRM}</span> {t.deleteAccount.typeAfter}</span>
                <input value={typed} onChange={(e) => setTyped(e.target.value)} className={field} autoComplete="off" spellCheck={false} />
              </label>
              <div className="flex gap-2">
                <button type="submit" disabled={busy || !password || typed.trim().toLowerCase() !== CONFIRM}
                  className="rounded-lg bg-rose-600 px-3.5 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-rose-700 disabled:opacity-50">
                  {busy ? t.deleteAccount.deleting : t.deleteAccount.submit}
                </button>
                <button type="button" onClick={() => { setOpen(false); setPassword(""); setTyped(""); setError(null); }}
                  className="rounded-lg px-3 py-1.5 text-sm text-zinc-600 hover:bg-zinc-100 dark:text-zinc-400 dark:hover:bg-zinc-800">
                  {t.common.cancel}
                </button>
              </div>
              {error && <p className="text-xs text-rose-600">{error}</p>}
            </form>
          )}
        </div>
      )}
    </section>
  );
}

function OrgList({ title, tone, orgs, note }: { title: string; tone: "amber" | "rose" | "zinc"; orgs: DeletionOrg[]; note: string }) {
  if (orgs.length === 0) return null;
  const color = tone === "amber" ? "text-amber-700 dark:text-amber-400" : tone === "rose" ? "text-rose-700 dark:text-rose-400" : "text-zinc-700 dark:text-zinc-300";
  return (
    <div className="space-y-1">
      <div className={`text-xs font-semibold ${color}`}>{title}</div>
      <ul className="list-disc pl-5 text-sm text-zinc-700 dark:text-zinc-300">
        {orgs.map((o) => <li key={o.tenant_id}>{o.name}</li>)}
      </ul>
      <p className="text-xs text-zinc-500">{note}</p>
    </div>
  );
}
