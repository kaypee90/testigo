export const API_BASE =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5180";

/** Fired when the API says the session is gone; the auth gate sends the user to sign in. */
export const UNAUTHORIZED_EVENT = "testigo:unauthorized";

/** The HTTP status is available without parsing a human-readable error message. */
export class ApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...init?.headers },
    cache: "no-store",
    // The session is an HttpOnly cookie on the API's origin.
    credentials: "include",
  });

  if (res.status === 401 && typeof window !== "undefined" && !path.startsWith("/api/auth/")) {
    window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
  }

  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new ApiError(`${init?.method ?? "GET"} ${path} failed: ${res.status} ${text}`, res.status);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

/** Turns "POST /api/... failed: 400 {"error":"..."}" into just the error text. */
export function apiErrorMessage(err: unknown): string {
  const text = err instanceof Error ? err.message : String(err);
  const json = text.slice(text.indexOf("{"));
  try {
    // Most errors are { error }; a few list every problem in { errors }.
    const body = JSON.parse(json) as { error?: string; errors?: string[] };
    // The first of errors is often error itself: say each problem once.
    return [...new Set([body.error, ...(body.errors ?? [])].filter(Boolean))].join(" ") || text;
  } catch {
    return text;
  }
}

// ---- Accounts and organizations (docs/platform.md) ----

export function getMe() {
  return apiFetch<import("./platformTypes").Me>("/api/auth/me");
}

/** A password sign-in either succeeds or asks for the two-factor code. */
export type SignInResult =
  | { user_id: string; tenant_id: string; two_factor_required?: false }
  | { two_factor_required: true; challenge: string; expires_in_seconds: number };

export function signIn(email: string, password: string) {
  return apiFetch<SignInResult>("/api/auth/login", { method: "POST", body: JSON.stringify({ email, password }) });
}

/** The second step of signing in: a code from the authenticator app, or a recovery code. */
export function signInWithCode(challenge: string, code: string) {
  return apiFetch<{ user_id: string; tenant_id: string }>("/api/auth/login/two-factor", { method: "POST", body: JSON.stringify({ challenge, code }) });
}

export type TwoFactorStatus = { enabled: boolean; enabled_at: string | null; recovery_codes_left: number };

export function getTwoFactor() {
  return apiFetch<TwoFactorStatus>("/api/auth/two-factor");
}

/** Starts setting up two-factor sign-in: the secret and a QR code (SVG) for the authenticator app. */
export function startTwoFactorSetup(password: string) {
  return apiFetch<{ secret: string; otpauth_uri: string; qr_svg: string }>("/api/auth/two-factor/setup", { method: "POST", body: JSON.stringify({ password }) });
}

/** Confirms the setup with the app's first code; returns the recovery codes, shown once. */
export function enableTwoFactor(code: string) {
  return apiFetch<{ recovery_codes: string[] }>("/api/auth/two-factor/enable", { method: "POST", body: JSON.stringify({ code }) });
}

export function disableTwoFactor(password: string, code: string) {
  return apiFetch<void>("/api/auth/two-factor/disable", { method: "POST", body: JSON.stringify({ password, code }) });
}

export function regenerateRecoveryCodes(password: string, code: string) {
  return apiFetch<{ recovery_codes: string[] }>("/api/auth/two-factor/recovery-codes", { method: "POST", body: JSON.stringify({ password, code }) });
}

/** Signs up. An open sign-up on a server that verifies email gets no account yet: a link to confirm
 * the address is emailed instead, and the account is created when it's opened (verifySignUp). */
export function signUp(input: { email: string; password: string; name?: string; organization?: string; invitation?: string; setup_token?: string }) {
  return apiFetch<{ user_id: string; tenant_id: string; verification_required?: undefined } | { verification_required: true; email: string; resend_id: string; expires_in_hours: number }>(
    "/api/auth/signup", { method: "POST", body: JSON.stringify(input) });
}

/** Opens the link from the confirmation email with the password chosen at sign-up: creates the
 * account and signs it in. */
export function verifySignUp(token: string, password: string) {
  return apiFetch<{ user_id: string; tenant_id: string }>("/api/auth/signup/verify", { method: "POST", body: JSON.stringify({ token, password }) });
}

/** Emails a new link for the sign-up `resendId` names (from signUp), at most once a minute. The
 * earlier link stops working once the new one is sent. It answers the same whether or not the
 * sign-up is still waiting; it fails only when the email couldn't be sent. */
export function resendSignUpVerification(resendId: string) {
  return apiFetch<{ sent: boolean }>("/api/auth/signup/resend", { method: "POST", body: JSON.stringify({ id: resendId }) });
}

export function signOut() {
  return apiFetch<void>("/api/auth/logout", { method: "POST" });
}

export function switchOrganization(tenantId: string) {
  return apiFetch<void>("/api/auth/switch", { method: "POST", body: JSON.stringify({ tenant_id: tenantId }) });
}

export type DeletionOrg = { tenant_id: string; name: string; role: string };
export type AccountDeletionPreview = {
  allowed: boolean;
  is_operator: boolean;
  /** Organizations the account leaves; their other members carry on. */
  leaves: DeletionOrg[];
  /** Organizations nobody else belongs to: closed and erased with the account. */
  closes: DeletionOrg[];
  /** Organizations it's the only owner of, with other members: they need another owner first. */
  blocks: DeletionOrg[];
};

export function getAccountDeletion() {
  return apiFetch<AccountDeletionPreview>("/api/account/deletion");
}

export function deleteAccount(password: string) {
  return apiFetch<{ organizations_left: number; organizations_closed: number }>("/api/account/delete", {
    method: "POST",
    body: JSON.stringify({ password }),
  });
}

export function changePassword(currentPassword: string, newPassword: string) {
  return apiFetch<void>("/api/auth/password", { method: "POST", body: JSON.stringify({ current_password: currentPassword, new_password: newPassword }) });
}

export function peekInvitation(token: string) {
  return apiFetch<{ organization: string; email: string; role: string }>(`/api/auth/invitations/${encodeURIComponent(token)}`);
}

export function acceptInvitation(token: string) {
  return apiFetch<{ tenant_id: string }>("/api/auth/invitations/accept", { method: "POST", body: JSON.stringify({ invitation: token }) });
}

export function getOrganization() {
  return apiFetch<import("./platformTypes").Organization>("/api/organization");
}

export function renameOrganization(name: string) {
  return apiFetch<void>("/api/organization", { method: "PATCH", body: JSON.stringify({ name }) });
}

export function listMembers() {
  return apiFetch<import("./platformTypes").Member[]>("/api/organization/members");
}

export function setMemberRole(userId: string, role: import("./platformTypes").Role) {
  return apiFetch<void>(`/api/organization/members/${userId}/role`, { method: "PUT", body: JSON.stringify({ role }) });
}

export function removeMember(userId: string) {
  return apiFetch<void>(`/api/organization/members/${userId}`, { method: "DELETE" });
}

export function listInvitations() {
  return apiFetch<import("./platformTypes").Invitation[]>("/api/organization/invitations");
}

export function inviteMember(email: string, role: import("./platformTypes").Role) {
  return apiFetch<import("./platformTypes").Invitation & { token: string }>("/api/organization/invitations", {
    method: "POST",
    body: JSON.stringify({ email, role }),
  });
}

export function revokeInvitation(invitationId: string) {
  return apiFetch<void>(`/api/organization/invitations/${invitationId}`, { method: "DELETE" });
}

// ---- Platform administration (docs/platform.md#platform-administration) ----

const qs = (params: Record<string, string | undefined>) => {
  const q = new URLSearchParams(Object.entries(params).filter((e): e is [string, string] => !!e[1])).toString();
  return q ? `?${q}` : "";
};

/** For someone locked out by two-factor sign-in: turns it off and signs out their browsers. */
export function adminResetTwoFactor(email: string) {
  return apiFetch<void>("/api/admin/users/two-factor/reset", { method: "POST", body: JSON.stringify({ email }) });
}

export function getAdminOverview() {
  return apiFetch<import("./platformTypes").AdminOverview>("/api/admin/overview");
}

export function listAdminOrganizations(q?: string, status?: string) {
  return apiFetch<import("./platformTypes").AdminOrganization[]>(`/api/admin/organizations${qs({ q, status })}`);
}

export function getAdminOrganization(tenantId: string) {
  return apiFetch<import("./platformTypes").AdminOrganizationDetail>(`/api/admin/organizations/${encodeURIComponent(tenantId)}`);
}

export function createAdminOrganization(input: { name: string; owner_email: string; plan_id?: string }) {
  return apiFetch<{ tenant_id: string; owner: import("./platformTypes").AddedPerson }>("/api/admin/organizations", { method: "POST", body: JSON.stringify(input) });
}

export function updateAdminOrganization(tenantId: string, input: { name?: string; plan_id?: string }) {
  return apiFetch<void>(`/api/admin/organizations/${encodeURIComponent(tenantId)}`, { method: "PATCH", body: JSON.stringify(input) });
}

export function suspendOrganization(tenantId: string, reason: string) {
  return apiFetch<void>(`/api/admin/organizations/${encodeURIComponent(tenantId)}/suspend`, { method: "POST", body: JSON.stringify({ reason }) });
}

export function reactivateOrganization(tenantId: string) {
  return apiFetch<void>(`/api/admin/organizations/${encodeURIComponent(tenantId)}/reactivate`, { method: "POST" });
}

export function adminAddMember(tenantId: string, email: string, role: import("./platformTypes").Role) {
  return apiFetch<import("./platformTypes").AddedPerson>(`/api/admin/organizations/${encodeURIComponent(tenantId)}/members`, {
    method: "POST",
    body: JSON.stringify({ email, role }),
  });
}

export function adminSetMemberRole(tenantId: string, userId: string, role: import("./platformTypes").Role) {
  return apiFetch<void>(`/api/admin/organizations/${encodeURIComponent(tenantId)}/members/${encodeURIComponent(userId)}/role`, {
    method: "PUT",
    body: JSON.stringify({ role }),
  });
}

export function adminRemoveMember(tenantId: string, userId: string) {
  return apiFetch<void>(`/api/admin/organizations/${encodeURIComponent(tenantId)}/members/${encodeURIComponent(userId)}`, { method: "DELETE" });
}

export function adminRevokeInvitation(tenantId: string, invitationId: string) {
  return apiFetch<void>(`/api/admin/organizations/${encodeURIComponent(tenantId)}/invitations/${encodeURIComponent(invitationId)}`, { method: "DELETE" });
}
