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
import { useI18n } from "@/lib/i18n/client";

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
  const { t, date } = useI18n();
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
    return <Box>{error ? <p className="text-xs text-rose-600">{error}</p> : <p className="text-xs text-zinc-500">{t.common.loading}</p>}</Box>;
  }

  return (
    <Box badge={status.enabled ? "On" : "Off"}>
      <p className="text-xs text-zinc-500">{t.twoFactor.intro}</p>

      {step.kind === "idle" && !status.enabled && (
        <button className={button} onClick={() => go({ kind: "password" })}>{t.twoFactor.enable}</button>
      )}

      {step.kind === "idle" && status.enabled && (
        <div className="space-y-2">
          <p className="text-xs text-zinc-600 dark:text-zinc-300">
            {t.twoFactor.activeSince(date(status.enabled_at))}{" "}
            <span className={status.recovery_codes_left <= 2 ? "font-medium text-amber-700 dark:text-amber-400" : ""}>
              {t.twoFactor.codesLeft(status.recovery_codes_left)}
            </span>
          </p>
          <div className="flex flex-wrap gap-2">
            <button className={subtle} onClick={() => go({ kind: "confirm", action: "regenerate" })}>{t.twoFactor.newCodes}</button>
            <button className={subtle} onClick={() => go({ kind: "confirm", action: "disable" })}>{t.twoFactor.disable}</button>
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
          <label htmlFor="two-factor-setup-password" className="text-xs text-zinc-500">{t.twoFactor.enterPassword}</label>
          <input id="two-factor-setup-password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder={t.twoFactor.password}
            autoComplete="current-password" className={field} autoFocus />
          <div className="flex gap-2">
            <button type="submit" className={button} disabled={busy || !password}>{busy ? t.common.busy : t.twoFactor.continue}</button>
            <button type="button" className={subtle} onClick={() => go({ kind: "idle" })}>{t.common.cancel}</button>
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
            <li>{t.twoFactor.scanStep}</li>
            <li>{t.twoFactor.codeStep}</li>
          </ol>
          {/* The QR code is drawn by the server as an SVG; shown as an image so none of it runs as markup. */}
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={`data:image/svg+xml;utf8,${encodeURIComponent(step.qrSvg)}`} alt={t.twoFactor.qrAlt}
            className="h-44 w-44 rounded-lg border border-zinc-200 bg-white p-2 dark:border-zinc-700" />
          <p className="text-xs text-zinc-500">
            {t.twoFactor.cantScan}{" "}
            <code className="select-all break-all font-mono text-zinc-800 dark:text-zinc-200">{step.secret.match(/.{1,4}/g)?.join(" ")}</code>
          </p>
          <div className="flex max-w-sm flex-col gap-2">
            <input value={code} onChange={(e) => setCode(e.target.value)} placeholder={t.twoFactor.sixDigits} aria-label={t.twoFactor.sixDigits} inputMode="numeric"
              autoComplete="one-time-code" maxLength={7} className={field} autoFocus />
            <div className="flex gap-2">
              <button type="submit" className={button} disabled={busy || code.replace(/\s/g, "").length !== 6}>{busy ? t.common.busy : t.twoFactor.activate}</button>
              <button type="button" className={subtle} onClick={() => go({ kind: "idle" })}>{t.common.cancel}</button>
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
              ? t.twoFactor.disableHelp
              : t.twoFactor.regenerateHelp}
          </label>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder={t.twoFactor.password} aria-label={t.twoFactor.password}
            autoComplete="current-password" className={field} autoFocus />
          <input value={code} onChange={(e) => setCode(e.target.value)} placeholder={t.twoFactor.appCode} aria-label={t.twoFactor.appOrRecoveryCode} autoComplete="one-time-code" className={field} />
          <div className="flex gap-2">
            <button type="submit" className={button} disabled={busy || !password || !code}>
              {busy ? t.common.busy : step.action === "disable" ? t.twoFactor.disable : t.twoFactor.generate}
            </button>
            <button type="button" className={subtle} onClick={() => go({ kind: "idle" })}>{t.common.cancel}</button>
          </div>
        </form>
      )}

      {error && <p className="text-xs text-rose-600">{error}</p>}
    </Box>
  );
}

function Box({ children, badge }: { children: React.ReactNode; badge?: "On" | "Off" }) {
  const { t } = useI18n();
  return (
    <section className="space-y-3 rounded-xl border border-zinc-200 bg-white p-5 shadow-sm dark:border-zinc-800 dark:bg-zinc-900/60">
      <div className="flex items-center gap-2">
        <h2 className="text-sm font-semibold">{t.twoFactor.title}</h2>
        {badge && (
          <span className={`rounded-full px-2 py-0.5 text-[11px] font-medium ${badge === "On"
            ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300"
            : "bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400"}`}>{badge === "On" ? t.twoFactor.on : t.twoFactor.off}</span>
        )}
      </div>
      {children}
    </section>
  );
}

/** Shown once: each code signs in once when the phone isn't at hand. */
function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const { t } = useI18n();
  const [copied, setCopied] = useState<"yes" | "failed" | null>(null);
  const text = codes.join("\n");

  function download() {
    const url = URL.createObjectURL(new Blob([`${t.twoFactor.fileHeader}\n\n${text}\n`], { type: "text/plain" }));
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
        {t.twoFactor.saveCodes}
      </div>
      <ul className="grid max-w-sm grid-cols-2 gap-1 rounded-lg bg-zinc-100 p-3 font-mono text-sm dark:bg-zinc-900">
        {codes.map((c) => <li key={c} className="select-all">{c}</li>)}
      </ul>
      <div className="flex flex-wrap gap-2">
        <button type="button" className={subtle} onClick={() => navigator.clipboard.writeText(text).then(() => setCopied("yes"), () => setCopied("failed"))}>
          {copied === "yes" ? t.twoFactor.copied : copied === "failed" ? t.twoFactor.copyFailed : t.common.copy}
        </button>
        <button type="button" className={subtle} onClick={download}>{t.twoFactor.download}</button>
        <button type="button" className={button} onClick={onDone}>{t.twoFactor.saved}</button>
      </div>
    </div>
  );
}
