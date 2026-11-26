"use client";

import { useState } from "react";
import { Icons } from "@/components/ui/icons";
import { ApiError, apiErrorMessage, verifySignUp } from "@/lib/api";

const field = "w-full rounded-lg border border-zinc-200 bg-white px-3 py-2.5 text-sm shadow-sm outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-500/20 dark:border-zinc-800 dark:bg-zinc-950";

/**
 * Where the link in a sign-up's confirmation email leads (docs/platform.md#email-verification).
 * Nothing happens on load: mail scanners open links to check them, and the link works once. The
 * person enters the password they chose at sign-up, which proves this is their sign-up and not one
 * someone else started with their address; then the account is created and Tasks opens.
 */
export default function VerifyEmailPage() {
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // A link that's expired, used or missing can't be tried again; a wrong password can (422).
  const [dead, setDead] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    const token = new URLSearchParams(window.location.search).get("token");
    if (!token) {
      setError("El enlace no tiene código. Ábrelo desde el correo o vuelve a registrarte para recibir otro.");
      setDead(true);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await verifySignUp(token, password);
      window.location.replace("/");
    } catch (err) {
      setError(apiErrorMessage(err));
      setDead(!(err instanceof ApiError && err.status === 422));
      setPassword("");
      setBusy(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-zinc-50 p-6 dark:bg-zinc-950">
      <div className="w-full max-w-sm space-y-4 rounded-2xl border border-zinc-200 bg-white p-8 shadow-xl dark:border-zinc-800 dark:bg-zinc-900">
        <a href="/login" aria-label="Testigo"><Icons.Logo className="h-9 w-9" /></a>
        {dead ? (
          <>
            <div>
              <h1 className="text-xl font-semibold tracking-tight">No pudimos confirmar tu correo</h1>
              <p className="mt-1 text-sm text-zinc-500" role="alert">
                {error}
              </p>
            </div>
            <a href="/login?mode=signup"
              className="block w-full rounded-lg bg-brand-500 py-2.5 text-center text-sm font-medium text-white shadow-sm hover:bg-brand-600">
              Volver a registrarme
            </a>
            <a href="/login" className="block w-full text-center text-xs text-brand-600 hover:underline dark:text-brand-400">
              ¿Ya tienes cuenta? Inicia sesión
            </a>
          </>
        ) : (
          <form onSubmit={submit} className="space-y-4">
            <div>
              <h1 className="text-xl font-semibold tracking-tight">Confirma tu correo</h1>
              <p className="mt-1 text-sm text-zinc-500">Ingresa la contraseña que elegiste al registrarte para terminar de crear la cuenta.</p>
            </div>
            <label className="block space-y-1.5">
              <span className="text-xs font-medium text-zinc-600 dark:text-zinc-400">Contraseña</span>
              <input type="password" required autoFocus autoComplete="current-password" value={password}
                onChange={(e) => setPassword(e.target.value)} className={field} />
            </label>
            {error && <p className="text-xs text-red-600 dark:text-red-400" role="alert">{error}</p>}
            <button type="submit" disabled={busy || !password}
              className="w-full rounded-lg bg-brand-500 py-2.5 text-sm font-medium text-white shadow-sm hover:bg-brand-600 disabled:opacity-50">
              {busy ? "Confirmando…" : "Confirmar correo"}
            </button>
          </form>
        )}
      </div>
    </div>
  );
}
