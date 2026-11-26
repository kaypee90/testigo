"use client";

import { useEffect, useState } from "react";
import {
  apiErrorMessage,
  disableTwoFactor,
  enableTwoFactor,
  getTwoFactor,
  regenerateRecoveryCodes,
  startTwoFactorSetup,
  type TwoFactorStatus,
} from "@/lib/api";

const field = "rounded-lg border border-zinc-200 bg-white px-3 py-1.5 text-sm shadow-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-500/20 dark:border-zinc-800 dark:bg-zinc-950";
const button = "rounded-lg bg-brand-500 px-3.5 py-1.5 text-sm font-medium text-white shadow-sm hover:bg-brand-600 disabled:opacity-50";
const subtle = "rounded-lg border border-zinc-200 px-2.5 py-1 text-xs hover:bg-zinc-100 disabled:opacity-50 dark:border-zinc-800 dark:hover:bg-zinc-800";

type Step =
  | { kind: "idle" }
  | { kind: "password" }
  | { kind: "scan"; secret: string; qrSvg: string }
  | { kind: "codes"; codes: string[] }
  | { kind: "confirm"; action: "disable" | "regenerate" };

/**
 * Two-factor sign-in (docs/platform.md#two-factor-sign-in): a code from an authenticator app after
 * the password. Setting up asks for the password, shows a QR code, confirms with the app's first
 * code, then shows recovery codes once.
 */
export function TwoFactorSettings() {
  const [status, setStatus] = useState<TwoFactorStatus | null>(null);
  const [step, setStep] = useState<Step>({ kind: "idle" });
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getTwoFactor().then(setStatus).catch((e) => setError(apiErrorMessage(e)));
  }, []);

  function go(next: Step) {
    setStep(next);
    setPassword("");
    setCode("");
    setError(null);
  }

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await action();
    } catch (e) {
      setError(apiErrorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  const refresh = async () => setStatus(await getTwoFactor());

  if (!status) {
    return <Box>{error ? <p className="text-xs text-rose-600">{error}</p> : <p className="text-xs text-zinc-500">Loading…</p>}</Box>;
  }

  return (
    <Box badge={status.enabled ? "On" : "Off"}>
      <p className="text-xs text-zinc-500">
        After your password, signing in also asks for a 6-digit code from an authenticator app on your phone, such as Google
        Authenticator, Microsoft Authenticator, 1Password or Authy. Someone who learns your password still can&apos;t get in.
      </p>

      {step.kind === "idle" && !status.enabled && (
        <button className={button} onClick={() => go({ kind: "password" })}>Turn on two-factor sign-in</button>
      )}

      {step.kind === "idle" && status.enabled && (
        <div className="space-y-2">
          <p className="text-xs text-zinc-600 dark:text-zinc-300">
            On since {status.enabled_at ? new Date(status.enabled_at).toLocaleDateString() : "—"}.{" "}
            <span className={status.recovery_codes_left <= 2 ? "font-medium text-amber-700 dark:text-amber-400" : ""}>
              {status.recovery_codes_left} recovery code{status.recovery_codes_left === 1 ? "" : "s"} left.
            </span>
          </p>
          <div className="flex flex-wrap gap-2">
            <button className={subtle} onClick={() => go({ kind: "confirm", action: "regenerate" })}>New recovery codes</button>
            <button className={subtle} onClick={() => go({ kind: "confirm", action: "disable" })}>Turn off</button>
          </div>
        </div>
      )}

      {step.kind === "password" && (
        <form className="flex max-w-sm flex-col gap-2" onSubmit={(e) => {
          e.preventDefault();
          run(async () => {
            const setup = await startTwoFactorSetup(password);
            go({ kind: "scan", secret: setup.secret, qrSvg: setup.qr_svg });
          });
        }}>
          <label className="text-xs text-zinc-500">Enter your password to start.</label>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="Password"
            autoComplete="current-password" className={field} autoFocus />
          <div className="flex gap-2">
            <button type="submit" className={button} disabled={busy || !password}>{busy ? "…" : "Continue"}</button>
            <button type="button" className={subtle} onClick={() => go({ kind: "idle" })}>Cancel</button>
          </div>
        </form>
      )}

      {step.kind === "scan" && (
        <form className="space-y-3" onSubmit={(e) => {
          e.preventDefault();
          run(async () => {
            const result = await enableTwoFactor(code);
            // The codes first: they're shown only this once, so a failed refresh mustn't hide them.
            go({ kind: "codes", codes: result.recovery_codes });
            refresh().catch(() => {});
          });
        }}>
          <ol className="list-decimal space-y-1 pl-5 text-xs text-zinc-600 dark:text-zinc-300">
            <li>In your authenticator app, add an account and scan this QR code.</li>
            <li>Then type the 6-digit code the app shows.</li>
          </ol>
          {/* The QR code is drawn by the server as an SVG; shown as an image so none of it runs as markup. */}
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={`data:image/svg+xml;utf8,${encodeURIComponent(step.qrSvg)}`} alt="QR code for your authenticator app"
            className="h-44 w-44 rounded-lg border border-zinc-200 bg-white p-2 dark:border-zinc-700" />
          <p className="text-xs text-zinc-500">
            Can&apos;t scan? Enter this key in the app instead:{" "}
            <code className="select-all break-all font-mono text-zinc-800 dark:text-zinc-200">{step.secret.match(/.{1,4}/g)?.join(" ")}</code>
          </p>
          <div className="flex max-w-sm flex-col gap-2">
            <input value={code} onChange={(e) => setCode(e.target.value)} placeholder="6-digit code" inputMode="numeric"
              autoComplete="one-time-code" maxLength={7} className={field} autoFocus />
            <div className="flex gap-2">
              <button type="submit" className={button} disabled={busy || code.replace(/\s/g, "").length !== 6}>{busy ? "…" : "Turn on"}</button>
              <button type="button" className={subtle} onClick={() => go({ kind: "idle" })}>Cancel</button>
            </div>
          </div>
        </form>
      )}

      {step.kind === "codes" && <RecoveryCodes codes={step.codes} onDone={() => go({ kind: "idle" })} />}

      {step.kind === "confirm" && (
        <form className="flex max-w-sm flex-col gap-2" onSubmit={(e) => {
          e.preventDefault();
          run(async () => {
            if (step.action === "disable") {
              await disableTwoFactor(password, code);
              await refresh();
              go({ kind: "idle" });
            } else {
              const result = await regenerateRecoveryCodes(password, code);
              go({ kind: "codes", codes: result.recovery_codes });
              refresh().catch(() => {});
            }
          });
        }}>
          <label className="text-xs text-zinc-500">
            {step.action === "disable"
              ? "To turn off two-factor sign-in, enter your password and a code from your app (or a recovery code)."
              : "New codes replace the old ones. Enter your password and a code from your app."}
          </label>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="Password"
            autoComplete="current-password" className={field} autoFocus />
          <input value={code} onChange={(e) => setCode(e.target.value)} placeholder="Code from your app" autoComplete="one-time-code" className={field} />
          <div className="flex gap-2">
            <button type="submit" className={button} disabled={busy || !password || !code}>
              {busy ? "…" : step.action === "disable" ? "Turn off" : "Make new codes"}
            </button>
            <button type="button" className={subtle} onClick={() => go({ kind: "idle" })}>Cancel</button>
          </div>
        </form>
      )}

      {error && <p className="text-xs text-rose-600">{error}</p>}
    </Box>
  );
}

function Box({ children, badge }: { children: React.ReactNode; badge?: "On" | "Off" }) {
  return (
    <section className="space-y-3 rounded-xl border border-zinc-200 bg-white p-5 shadow-sm dark:border-zinc-800 dark:bg-zinc-900/60">
      <div className="flex items-center gap-2">
        <h2 className="text-sm font-semibold">Two-factor sign-in</h2>
        {badge && (
          <span className={`rounded-full px-2 py-0.5 text-[11px] font-medium ${badge === "On"
            ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300"
            : "bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400"}`}>{badge}</span>
        )}
      </div>
      {children}
    </section>
  );
}

/** Shown once: each code signs in once when the phone isn't at hand. */
function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const [copied, setCopied] = useState<"yes" | "failed" | null>(null);
  const text = codes.join("\n");

  function download() {
    const url = URL.createObjectURL(new Blob([`Testigo recovery codes\nEach code works once.\n\n${text}\n`], { type: "text/plain" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = "testigo-recovery-codes.txt";
    a.click();
    // Some browsers start the download after click() returns; revoking straight away can cancel it.
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  return (
    <div className="space-y-3">
      <div className="rounded border border-amber-300 bg-amber-50 p-3 text-xs text-amber-900 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-200">
        Save these recovery codes somewhere safe, such as your password manager. If you lose your phone, each one signs you in
        once. They won&apos;t be shown again.
      </div>
      <ul className="grid max-w-sm grid-cols-2 gap-1 rounded-lg bg-zinc-100 p-3 font-mono text-sm dark:bg-zinc-900">
        {codes.map((c) => <li key={c} className="select-all">{c}</li>)}
      </ul>
      <div className="flex flex-wrap gap-2">
        <button type="button" className={subtle} onClick={() => navigator.clipboard.writeText(text).then(() => setCopied("yes"), () => setCopied("failed"))}>
          {copied === "yes" ? "Copied" : copied === "failed" ? "Couldn't copy: use Download" : "Copy"}
        </button>
        <button type="button" className={subtle} onClick={download}>Download</button>
        <button type="button" className={button} onClick={onDone}>I&apos;ve saved them</button>
      </div>
    </div>
  );
}
