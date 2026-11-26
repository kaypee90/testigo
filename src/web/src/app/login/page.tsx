"use client";

import { Icons } from "@/components/ui/icons";

import { useEffect, useState } from "react";
import { acceptInvitation, apiErrorMessage, getMe, peekInvitation, resendSignUpVerification, signIn, signInWithCode, signUp } from "@/lib/api";
import type { Me } from "@/lib/platformTypes";
import type { Role } from "@/lib/platformTypes";
import { useI18n } from "@/lib/i18n/client";
import { LanguageSwitch } from "@/components/shell/LanguageSwitch";

const field = "w-full rounded-lg border border-zinc-200 bg-white px-3 py-2.5 text-sm shadow-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-500/20 dark:border-zinc-800 dark:bg-zinc-950";

/** Where signing in lands without a ?next=: the case queue. */
const HOME = "/casos";

/** Only same-site paths, so a crafted ?next= can't send someone to another site after sign-in. */
function safeNext(value: string | null) {
  return value && value.startsWith("/") && !value.startsWith("//") ? value : HOME;
}

export default function LoginPage() {
  const { t } = useI18n();
  const [mode, setMode] = useState<"signin" | "signup">("signin");
  const [me, setMe] = useState<Me | null>(null);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [name, setName] = useState("");
  const [organization, setOrganization] = useState("");
  const [invitation, setInvitation] = useState<string | null>(null);
  // A server with no accounts yet: its first one takes the operator's setup token.
  const [setupToken, setSetupToken] = useState("");
  const [invitedTo, setInvitedTo] = useState<{ organization: string; role: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // The server couldn't be reached; worded at render time so it follows a language switch.
  const [cantConnect, setCantConnect] = useState(false);
  // Two-factor sign-in: after a right password, the code step.
  const [challenge, setChallenge] = useState<string | null>(null);
  const [code, setCode] = useState("");
  // A recovery code has letters: a full keyboard on phones, not the number pad.
  const [useRecovery, setUseRecovery] = useState(false);
  // An open sign-up waiting for its email to be confirmed: where the link went.
  const [sentTo, setSentTo] = useState<{ email: string; resendId: string; hours: number } | null>(null);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const token = params.get("invite");
    getMe()
      .then(async (m) => {
        setMe(m);
        if (m.server.auth_mode === "disabled") {
          window.location.replace(safeNext(params.get("next")));
          return;
        }

        if (token) {
          setInvitation(token);
          try {
            const peek = await peekInvitation(token);
            setInvitedTo({ organization: peek.organization, role: peek.role });
            setEmail(peek.email);
            if (m.authenticated) {
              // Already signed in: join straight away.
              await acceptInvitation(token);
              window.location.replace(HOME);
              return;
            }
            setMode("signup");
          } catch (err) {
            setError(apiErrorMessage(err));
          }
        } else if (m.authenticated) {
          window.location.replace(safeNext(params.get("next")));
        } else if (params.get("mode") === "signup" || m.server.setup_required) {
          setMode("signup");
        }
      })
      .catch(() => setCantConnect(true));
  }, []);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (challenge) {
        await signInWithCode(challenge, code);
      } else if (mode === "signin") {
        const result = await signIn(email, password);
        if (result.two_factor_required) {
          setChallenge(result.challenge);
          setPassword("");
          setBusy(false);
          return;
        }
      } else {
        const result = await signUp({ email, password, name: name || undefined, organization: organization || undefined, invitation: invitation ?? undefined, setup_token: setupToken.trim() || undefined });
        if (result.verification_required) {
          setSentTo({ email: result.email, resendId: result.resend_id, hours: result.expires_in_hours });
          setPassword("");
          setBusy(false);
          return;
        }
      }
      // So the server lets the case queue through instead of sending it to the landing page.
      window.location.replace(safeNext(new URLSearchParams(window.location.search).get("next")));
    } catch (err) {
      const message = apiErrorMessage(err);
      // An expired sign-in, or too many wrong codes: back to the password.
      if (challenge && /password again/i.test(message)) {
        setChallenge(null);
        setCode("");
        setUseRecovery(false);
      }
      setError(message);
      setBusy(false);
    }
  }

  const shownError = error ?? (cantConnect ? t.errors.cantConnect : null);
  const setupRequired = !!me?.server.setup_required && !invitation;
  const signupAvailable = !!invitation || setupRequired || me?.server.signup_allowed !== false;

  return (
    <div className="grid min-h-screen lg:grid-cols-2">
      <div className="relative hidden overflow-hidden bg-brand-900 p-12 text-white lg:flex lg:flex-col lg:justify-between">
        <div className="pointer-events-none absolute -left-32 -top-32 h-96 w-96 rounded-full bg-brand-400/30 blur-3xl" />
        <div className="pointer-events-none absolute -bottom-40 right-0 h-96 w-96 rounded-full bg-brand-500/30 blur-3xl" />
        <a href="/login" className="relative flex items-center gap-3">
          <Icons.Logo className="h-9 w-9" />
          <span className="text-lg font-semibold tracking-tight">Testigo</span>
        </a>
        <div className="relative max-w-md space-y-4">
          <h2 className="text-3xl font-semibold leading-tight tracking-tight">{t.login.tagline}</h2>
          <ul className="space-y-2 text-sm text-brand-100">
            <li className="flex gap-2"><Icons.Check className="h-4 w-4 text-brand-300" /> {t.login.point1}</li>
            <li className="flex gap-2"><Icons.Check className="h-4 w-4 text-brand-300" /> {t.login.point2}</li>
            <li className="flex gap-2"><Icons.Check className="h-4 w-4 text-brand-300" /> {t.login.point3}</li>
          </ul>
        </div>
        <div className="relative text-xs text-brand-200">{t.login.synthetic}</div>
      </div>

      <div className="relative flex items-center justify-center bg-zinc-50 p-6 pt-16 dark:bg-zinc-950">
      <LanguageSwitch className="absolute right-4 top-4" />
      {sentTo ? (
        <CheckYourEmail email={sentTo.email} resendId={sentTo.resendId} hours={sentTo.hours} onBack={() => { setSentTo(null); setError(null); }} />
      ) : challenge ? (
      <form onSubmit={submit} className="w-full max-w-sm space-y-4 rounded-2xl border border-zinc-200 bg-white p-8 shadow-xl dark:border-zinc-800 dark:bg-zinc-900">
        <div>
          <a href="/login" aria-label="Testigo"><Icons.Logo className="mb-4 h-9 w-9 lg:hidden" /></a>
          <h1 className="text-xl font-semibold tracking-tight">{useRecovery ? t.login.recoveryTitle : t.login.codeTitle}</h1>
          <p className="mt-1 text-sm text-zinc-500">
            {useRecovery ? t.login.recoveryHelp : t.login.codeHelp}
          </p>
        </div>
        <input key={useRecovery ? "recovery" : "app"} value={code} onChange={(e) => setCode(e.target.value)}
          placeholder={useRecovery ? "xxxxx-xxxxx" : "123 456"} inputMode={useRecovery ? "text" : "numeric"}
          autoComplete={useRecovery ? "off" : "one-time-code"} autoCapitalize="none" spellCheck={false} required autoFocus
          aria-label={useRecovery ? t.login.recoveryCode : t.login.appCode}
          className={`${field} text-center font-mono text-lg tracking-widest`} />
        <button type="button" onClick={() => { setUseRecovery(!useRecovery); setCode(""); setError(null); }}
          className="text-xs text-brand-600 hover:underline dark:text-brand-400">
          {useRecovery ? t.login.useApp : t.login.useRecovery}
        </button>
        {shownError && <div className="text-xs text-rose-600">{shownError}</div>}
        <button type="submit" disabled={busy || !code.trim()} className="w-full rounded-lg bg-brand-500 py-2.5 text-sm font-medium text-white shadow-sm hover:bg-brand-600 disabled:opacity-50">
          {busy ? t.common.busy : t.login.signIn}
        </button>
        <button type="button" onClick={() => { setChallenge(null); setCode(""); setUseRecovery(false); setError(null); }}
          className="w-full text-center text-xs text-brand-600 hover:underline dark:text-brand-400">
          {t.common.back}
        </button>
      </form>
      ) : (
      <form onSubmit={submit} className="w-full max-w-sm space-y-4 rounded-2xl border border-zinc-200 bg-white p-8 shadow-xl dark:border-zinc-800 dark:bg-zinc-900">
        <div>
          <a href="/login" aria-label="Testigo"><Icons.Logo className="mb-4 h-9 w-9 lg:hidden" /></a>
          <h1 className="text-xl font-semibold tracking-tight">{mode === "signin" ? t.login.signIn : t.login.createAccount}</h1>
          <p className="mt-1 text-sm text-zinc-500">
            {invitedTo
              ? t.login.invited(invitedTo.organization, t.roles.labels[invitedTo.role as Role] ?? invitedTo.role)
              : mode === "signin" ? t.login.signInHelp : t.login.signUpHelp}
          </p>
        </div>

        {mode === "signup" && (
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder={t.login.yourName} aria-label={t.login.yourName} autoComplete="name" className={field} />
        )}
        {/* Sign-in also takes a username (the local admin), so it's not an email-only field. */}
        <input value={email} onChange={(e) => setEmail(e.target.value)} placeholder={mode === "signin" ? t.login.emailOrUser : t.login.email} aria-label={mode === "signin" ? t.login.emailOrUser : t.login.email}
          type={mode === "signin" ? "text" : "email"} required autoComplete={mode === "signin" ? "username" : "email"}
          readOnly={!!invitedTo} className={field} />
        <input value={password} onChange={(e) => setPassword(e.target.value)} placeholder={mode === "signup" ? t.login.passwordNew : t.login.password} aria-label={t.login.password}
          type="password" required autoComplete={mode === "signup" ? "new-password" : "current-password"} className={field} />
        {mode === "signup" && setupRequired && (
          <div className="space-y-1">
            <input value={setupToken} onChange={(e) => setSetupToken(e.target.value)} placeholder={t.login.setupToken} aria-label={t.login.setupToken} required autoComplete="off" className={field} />
            <p className="text-xs text-zinc-500">{t.login.setupHelp}</p>
          </div>
        )}
        {mode === "signup" && !invitedTo && (
          <input value={organization} onChange={(e) => setOrganization(e.target.value)} placeholder={t.login.organizationOptional} aria-label={t.login.organization} className={field} />
        )}

        {mode === "signup" && !setupRequired && me?.server.email_verification && (
          <p className="text-xs text-zinc-500">{t.login.verificationNote}</p>
        )}

        {shownError && <div className="text-xs text-rose-600">{shownError}</div>}

        <button type="submit" disabled={busy} className="w-full rounded-lg bg-brand-500 py-2.5 text-sm font-medium text-white shadow-sm hover:bg-brand-600 disabled:opacity-50">
          {busy ? t.common.busy : mode === "signin" ? t.login.signIn : invitedTo ? t.login.createAndJoin : t.login.create}
        </button>

        {!invitedTo && signupAvailable && (
          <button type="button" onClick={() => { setMode(mode === "signin" ? "signup" : "signin"); setError(null); }}
            className="w-full text-center text-xs text-brand-600 hover:underline dark:text-brand-400">
            {mode === "signin" ? t.login.noAccount : t.login.haveAccount}
          </button>
        )}
        {invitedTo && mode === "signup" && (
          <button type="button" onClick={() => setMode("signin")} className="w-full text-center text-xs text-brand-600 hover:underline dark:text-brand-400">
            {t.login.haveAccountInvited}
          </button>
        )}
      </form>
      )}
      </div>
    </div>
  );
}

/**
 * After an open sign-up on a server that verifies email: the account is created when the link sent
 * to the address is opened. A new link can be sent once a minute.
 */
function CheckYourEmail({ email, resendId, hours, onBack }: { email: string; resendId: string; hours: number; onBack: () => void }) {
  const { t } = useI18n();
  const [wait, setWait] = useState(60);
  const [note, setNote] = useState<string | null>(null);
  useEffect(() => {
    if (wait <= 0) return;
    const timer = setTimeout(() => setWait((w) => w - 1), 1000);
    return () => clearTimeout(timer);
  }, [wait]);

  async function resend() {
    setNote(null);
    try {
      await resendSignUpVerification(resendId);
      setNote(t.login.resent);
      setWait(60);
    } catch (err) {
      setNote(apiErrorMessage(err));
    }
  }

  return (
    <div className="w-full max-w-sm space-y-4 rounded-2xl border border-zinc-200 bg-white p-8 shadow-xl dark:border-zinc-800 dark:bg-zinc-900">
      <a href="/login" aria-label="Testigo"><Icons.Logo className="mb-4 h-9 w-9 lg:hidden" /></a>
      <span className="flex h-11 w-11 items-center justify-center rounded-xl bg-brand-500/10 text-brand-500">
        <Icons.Mail className="h-5 w-5" />
      </span>
      <div>
        <h1 className="text-xl font-semibold tracking-tight">{t.login.checkEmail}</h1>
        <p className="mt-1 text-sm text-zinc-500">
          {t.login.sentBefore} <span className="font-medium text-zinc-800 dark:text-zinc-200">{email}</span>{t.login.sentAfter(hours)}
        </p>
      </div>
      <p className="text-xs text-zinc-500">{t.login.notArrived}</p>
      {note && <p className="text-xs text-zinc-600 dark:text-zinc-400" role="status">{note}</p>}
      <button type="button" onClick={() => void resend()} disabled={wait > 0}
        className="w-full rounded-lg border border-zinc-200 py-2.5 text-sm font-medium text-zinc-700 hover:bg-zinc-50 disabled:opacity-50 dark:border-zinc-700 dark:text-zinc-200 dark:hover:bg-zinc-800">
        {wait > 0 ? t.login.resendIn(wait) : t.login.resend}
      </button>
      <button type="button" onClick={onBack} className="w-full text-center text-xs text-brand-600 hover:underline dark:text-brand-400">
        {t.login.otherEmail}
      </button>
    </div>
  );
}
