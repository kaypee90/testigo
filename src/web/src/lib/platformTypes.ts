export type Role = "Viewer" | "Member" | "Admin" | "Owner";

export const ROLES: Role[] = ["Viewer", "Member", "Admin", "Owner"];
// Role names and what each may do are in lib/i18n (roles.labels, roles.help). Reading report
// content takes a Testigo staff role on top (Analyst or Reviewer), which an Admin doesn't get by
// being Admin (NFR-SEC-8).

export function atLeast(role: string | undefined, minimum: Role) {
  return ROLES.indexOf((role ?? "Viewer") as Role) >= ROLES.indexOf(minimum);
}

export interface Me {
  authenticated: boolean;
  server: { auth_mode: "accounts" | "disabled"; signup_allowed: boolean; email_verification?: boolean; setup_required?: boolean };
  user?: { user_id: string | null; email: string | null; platform_admin: boolean; two_factor_enabled?: boolean };
  via?: "session" | "disabled";
  tenant_id?: string;
  role?: Role;
  organizations?: { tenant_id: string; name: string; role: Role; suspended?: boolean }[];
}

export interface Organization {
  tenant_id: string;
  name: string;
  created_at: string | null;
  your_role: Role;
  members: number;
}

export interface Member {
  user_id: string;
  email: string;
  name: string;
  role: Role;
  joined_at: string;
}

export interface Invitation {
  invitation_id: string;
  email: string;
  role: Role;
  created_at?: string;
  expires_at: string;
  accepted_at?: string | null;
  revoked_at?: string | null;
}

// ---- Platform administration (docs/platform.md#platform-administration) ----

export type OrgStatus = "Active" | "Suspended";

export interface AdminOrganization {
  tenant_id: string;
  name: string;
  status: OrgStatus;
  created_at: string;
  members: number;
  pending_invitations: number;
  owner_email: string | null;
  last_active_at: string | null;
  suspended_at: string | null;
  suspended_reason: string | null;
}

export interface AdminMember {
  user_id: string;
  email: string;
  name: string;
  role: Role;
  joined_at: string;
  last_login_at: string | null;
}

export interface AdminOrganizationDetail {
  organization: AdminOrganization;
  members: AdminMember[];
  invitations: Invitation[];
}

/** What adding someone did: joined straight away (existing account) or invited (link to share). */
export interface AddedPerson {
  email: string;
  role: Role;
  joined: boolean;
  invitation_id: string | null;
  invite_url: string | null;
  email_sent: boolean;
}

export interface AdminOverview {
  organizations: number;
  active_organizations: number;
  suspended_organizations: number;
  members: number;
  email_enabled: boolean;
  /** Which build of the server is running. A commit is null when the build wasn't stamped. */
  version?: { commit: string | null; short_commit: string | null; built_at: string | null; started_at: string };
  recent_activity: { at: string; actor: string; action: string; target: string; summary: string }[];
}
