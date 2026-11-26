import { ApiError, apiFetch } from "./api";
import type { Messages } from "./i18n/languages";

/**
 * The Testigo case API (src/AgentRuntime.Api/Controllers/TestigoCasesController.cs): the queue,
 * the case view and the decisions a person makes on them (FR-CASE-1 to FR-CASE-4). Nothing here
 * publishes, sends or deletes; agents never call it.
 */

export type CaseStatus =
  | "received" | "in_triage" | "needs_info" | "verified" | "referred" | "in_litigation" | "closed_no_action" | "withdrawn";

// Status, analysis, link and redaction labels are in lib/i18n (status, analysis, links, redaction).

export interface TestigoMe {
  allowed: boolean;
  problem: "two_factor_required" | "not_a_person" | "no_role" | null;
  roles: string[];
  organization_admin: boolean;
}

export interface TermView { dimension: string; term: string; label: string; state: string }

export interface CaseSummary {
  id: string;
  number: string;
  status: string;
  immediate_danger: boolean;
  danger_source: string | null;
  analysis_state: string;
  country: string | null;
  language: string;
  submitted_at: string;
  assignee_id: string | null;
  pending_suggestions: number;
  terms: TermView[];
}

export interface Suggestion {
  id: string;
  kind: "term" | "field" | "danger";
  dimension: string;
  term: string | null;
  label: string | null;
  value: string | null;
  quote: string | null;
  start: number | null;
  length: number | null;
  source: "ai" | "analyst" | "rules";
  state: "suggested" | "confirmed" | "rejected";
  model: string | null;
  prompt_version: string | null;
  taxonomy_version: string;
  created_at: string;
  decided_by: string | null;
  decided_at: string | null;
}

export interface CaseLink {
  id: string;
  other_case_id: string;
  other_number: string;
  other_status: string;
  kind: string;
  score: number;
  state: "suggested" | "confirmed" | "rejected";
  decided_by: string | null;
}

export interface CaseView {
  summary: CaseSummary;
  text: string;
  redacted_text: string | null;
  redaction_types: string[];
  redaction_method: string | null;
  answers: { field: string; label: string; values: string[] }[];
  suggestions: Suggestion[];
  links: CaseLink[];
  history: { from: string; to: string; by_user_id: string; at: string; reason: string }[];
  consent: { notice_version: string; processing: boolean; contact: boolean; publication: boolean; recorded_at: string; withdrawn_at: string | null; has_contact_details: boolean };
  analysis_note: string | null;
  severity: string;
  verified_by: string | null;
  verified_at: string | null;
  allowed_statuses: string[];
  form_version: string;
  channel: string;
}

export interface Taxonomy {
  version: string;
  status: string;
  dimensions: { key: string; label: string; multiple: boolean; terms: { key: string; label: string; description: string | null }[] }[];
}

export interface AccessEntry { at: string; actor_type: string; actor_id: string; actor_name: string; action: string; summary: string; outcome: string }
export interface StaffMember { user_id: string; email: string; name: string; organization_role: string; two_factor: boolean; roles: string[] }

export interface QueueFilter {
  status?: string;
  term?: string;
  country?: string;
  assignee?: string;
  from?: string;
  to?: string;
  danger?: boolean;
  limit?: number;
  offset?: number;
}

const post = <T,>(path: string, body: unknown) => apiFetch<T>(path, { method: "POST", body: JSON.stringify(body) });

export const testigoMe = () => apiFetch<TestigoMe>("/api/testigo/me");
export const getTaxonomy = () => apiFetch<Taxonomy>("/api/testigo/taxonomy");

export function listCases(filter: QueueFilter) {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(filter)) {
    if (v === undefined || v === "" || v === false) continue;
    q.set(k, String(v));
  }
  return apiFetch<{ items: CaseSummary[]; total: number }>(`/api/testigo/cases?${q}`);
}

export const getCase = (id: string) => apiFetch<CaseView>(`/api/testigo/cases/${encodeURIComponent(id)}`);

export const decideSuggestion = (caseId: string, suggestionId: string, action: "confirm" | "reject" | "edit", term?: string, value?: string) =>
  post<{ ok: true }>(`/api/testigo/cases/${caseId}/suggestions/${suggestionId}`, { action, term, value });

export const addClassification = (caseId: string, dimension: string, term: string) =>
  post<{ id: string }>(`/api/testigo/cases/${caseId}/classifications`, { dimension, term });

export const changeStatus = (caseId: string, to: string, reason: string) =>
  post<{ status: string }>(`/api/testigo/cases/${caseId}/status`, { to, reason });

export const decideLink = (caseId: string, linkId: string, action: "confirm" | "reject") =>
  post<{ ok: true }>(`/api/testigo/cases/${caseId}/links/${linkId}`, { action });

export const assignCase = (caseId: string, assigneeId: string | null) =>
  post<{ ok: true }>(`/api/testigo/cases/${caseId}/assign`, { assignee_id: assigneeId });

export const revealContact = (caseId: string, reason: string) =>
  post<{ method: string; value: string }>(`/api/testigo/cases/${caseId}/contact`, { reason });

export const caseAccessLog = (caseId: string) => apiFetch<{ items: AccessEntry[] }>(`/api/testigo/cases/${caseId}/access`);
export const listStaff = () => apiFetch<{ items: StaffMember[] }>("/api/testigo/staff");

/** The API's own message from an error ("{ error: … }"), or a generic one in the dashboard's language. */
export function errorText(e: unknown, t: Messages): string {
  if (e instanceof ApiError) {
    const json = e.message.indexOf("{");
    if (json >= 0) {
      try {
        const body = JSON.parse(e.message.slice(json)) as { error?: string };
        if (body.error) return body.error;
      } catch { /* not JSON */ }
    }
    if (e.status === 404) return t.errors.caseNotFound;
    if (e.status === 401) return t.errors.sessionEnded;
  }
  return t.errors.generic;
}
