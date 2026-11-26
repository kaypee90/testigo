# Threat model (draft)

NFR-SEC-1 asks for this before the intake is built for real (Phase 1). This is the Phase 0 draft:
it describes the prototype as built, the gaps, and what Phase 1 must close. It has **not** been
reviewed by a deploying organization or a security auditor; both are planned, the
external audit in Phase 3.

People who report may be at risk from powerful companies or governments. When security and
convenience conflict, security wins (requirements §3, rule 8).

## Assets

| Asset | Where it lives | Protection now |
|---|---|---|
| Report text and form answers | `testigo.reports` | AES-256-GCM per field, data key per organization, bound to the row (associated data) |
| Contact details | `testigo.contacts` | As above, with a **separate** contact key; read only through a recorded "reveal" with a reason |
| Redacted copies and redaction maps | `testigo.redacted_copies` | Encrypted like report text |
| AI suggestions, quotes, field values | `testigo.classifications` | Values and quotes encrypted |
| Status-change reasons | `testigo.status_changes` | Encrypted |
| Case codes and passphrases | `testigo.reports` | Only an HMAC of the code (lookup key) and an Argon2id hash of the passphrase |
| Master keys | A key file outside the database (`/app/keys`), or configuration | File mode 0600; never in the database or backups of it |
| Intake shared key | `/app/intake` volume, read-only in the reporter app | Compared in constant time on a hash; anything else is a 404 |
| Reporter identity (IP, device) | Nowhere, by design | Not logged, not traced, not forwarded (see below) |
| Staff accounts and sessions | Aktor identity tables | Passwords hashed, 2FA required for case data, sessions of 8 hours |
| Audit log | Aktor's hash-chained audit log | Append-only chain; entries hold no report content |

## Trust boundaries

```
reporter's browser ──HTTPS──▶ Caddy ──▶ reporter app (own origin, no session, no case data)
                                          │ internal network, shared key
                                          ▼
staff browser ──HTTPS──▶ Caddy ──▶ staff dashboard ──▶ API (sessions, roles, 2FA) ──▶ Postgres
                                                        │
                                                        ▼
                                             triage worker ──▶ model (Mock, or local Ollama)
```

1. **Internet → reporter app.** Anonymous, untrusted. The reporter app can only call the API's
   `/api/intake/*` endpoints, with a key it reads from a read-only volume. Caddy answers 404 to
   `/api/intake*` on the public API domain.
2. **Reporter app → API.** The shared key, on the internal network.
3. **Staff browser → API.** Session cookie on the API origin; roles checked on every request.
4. **Report text → model.** Report text is untrusted input; the model is untrusted output.
5. **API → Postgres.** The database holds ciphertext for sensitive fields; keys live elsewhere.

## Adversaries and mitigations

### The reported company or government

Wants to know who reported, or to read or destroy reports.

- No reporter IP or user agent is stored or logged: the intake endpoints are excluded from request
  tracing and from the staff activity log (`ReporterPaths`); the reporter app logs no requests and
  sends the API no client address or forwarded headers. Rate limiting uses in-memory buckets keyed
  by an HMAC of the address under a salt that rotates hourly and is never stored (NFR-SEC-3).
- Field-level encryption, so a database dump or backup alone reveals no report content (NFR-SEC-4).
- Legal pressure on the operator is out of scope for code; it belongs in the host's legal
  analysis ([`data-protection.md`](data-protection.md)).
- **Gap:** no Tor onion address yet (NFR-SEC-6, optional); traffic analysis of the reporter
  domain is possible.

### A hostile insider (staff)

- Roles checked server-side on every case endpoint; Analyst or Reviewer needed; organization
  admins can't read case content without one of those roles (NFR-SEC-8).
- Two-factor sign-in required for case data (on by default).
- Every case read, decision, status change, assignment and contact reveal is recorded in the
  hash-chained audit log with the person and a reason; Reviewers can see who read a case
  (NFR-SEC-10).
- Contact details need a separate, recorded reveal with a reason.
- **Gap:** an insider with database and key-file access can decrypt everything. Splitting key
  custody (KMS, or client-side encryption, NFR-SEC-5, a DECISION for Phase 2) is the mitigation.

### Someone with the reporter's device

- Quick-exit button on every page, and Escape twice; `location.replace` so the page isn't one
  "back" away (FR-INT-5).
- Pages are `no-store` and `noindex`; the acknowledgement is the POST's own response, so the case
  code never appears in a URL or the history.
- The case code and passphrase are shown once and not stored in the browser.
- **Gap:** browser history still shows the reporter domain. The intro page says so.

### A network observer

- HTTPS with HSTS through Caddy (production overlay); `Referrer-Policy: no-referrer`.
- The reporter app loads nothing from any other origin: CSP `default-src 'none'`, only
  same-origin scripts and styles, no inline code, no fonts, images or analytics from elsewhere
  (NFR-SEC-2, NFR-SEC-6). Tests check every page.

### A compromised server

- Master keys in a file outside the database; a compromised database alone isn't enough.
- **Gap:** a compromised API host has the keys in memory and can read everything. Client-side
  encryption (NFR-SEC-5) is the only full mitigation and is a Phase 2 DECISION.
- The API container has no Docker socket (it was removed with the Aktor pipeline script stages).

### A malicious "report" (against staff or agents)

- Report text is rendered as text, never HTML, in the staff dashboard.
- Triage makes one bounded model call with a single tool, `submit_triage`. Code validates every
  term against the taxonomy, checks every quote against the text the model saw, and drops what it
  can't ground. There is no retry loop; on failure the case is marked "analysis incomplete" for a
  person (NFR-AI-4, NFR-AI-5).
- Agents have no tool that publishes, sends, deletes or changes permissions (NFR-SEC-12); the
  triage path has no tools at all beyond `submit_triage`.
- The eval set includes injection attempts (stop triaging, change status and send email, stop
  redaction); `testigo-eval` checks that none changes the model's behavior.
- Report text is wrapped in neutral delimiters, and delimiter look-alikes in the report are
  neutralized before the model sees them.

### Abuse of the intake (spam, flooding)

- Per-client limit in the reporter app, a global limit in the API, a 64 KB request limit, and
  antiforgery tokens on every form POST.

## What Phase 1 must add

1. Review this document with the deploying organization, then with a security auditor (Phase 3).
2. Case-code follow-up with constant-time lookups, per-case and global rate limits, and lockout
   (NFR-SEC-9); the code and passphrase primitives exist, the endpoints don't.
3. A DECISION on client-side encryption (NFR-SEC-5) with this threat model.
4. Optional Tor onion address for the reporter app.
5. Documented steps for checking, on a running deployment, that no reporter IP is logged
   (NFR-SEC-3): Caddy access logs off, API and reporter logs reviewed.
6. Container images built in CI with an SBOM (NFR-SEC-13).
