# Open questions

These items are assumptions about what a deploying organization needs. No organization has reviewed this
project; nothing here has been agreed with them. Implemented assumptions use data files or
settings where possible. Unbuilt features and proposed governance are identified separately.

Status: **open** for all of them until reviewed with a deploying organization.

| # | Question | Requirement | Current prototype value | Where |
|---|---|---|---|---|
| Q1 | Which harm taxonomy terms, and how do the "rights affected" categories line up with the ones human rights organizations use in litigation and standards work? | §4, FR-ANA-1 | Starter taxonomy v1 with 5 dimensions (technology, harm type, rights, affected, actor type); severity is set by an analyst | `src/Testigo/Data/taxonomy-v1.json`, or `Testigo:Data:Taxonomy` to point at another file |
| Q2 | What should the report form ask, in which order and with which wording? Reading level, trauma-informed wording (NFR-UX-3, NFR-UX-4) | FR-INT-1, FR-INT-2 | Form v1: free text required, everything else optional; region no finer than region | `src/Testigo/Data/form-v1.json`, or `Testigo:Data:Form`; reporter page texts in `src/Testigo.Reporter/Texts/es.json` |
| Q3 | The notice before reporting: what happens to a report, who reads it, the AI step, the risks | FR-INT-2 | Draft text on the reporter start page | `src/Testigo.Reporter/Texts/es.json` (`intro.*`), notice version in the form file |
| Q4 | Emergency and support contacts per country, and the response time promised to reporters | FR-INT-9, FR-ANA-6 | Chilean public emergency numbers (133, 131, 132), the Ministry of Health suicide-prevention line (*4141, to confirm), and "por confirmar" placeholders for psychological and legal support; "this prototype doesn't answer reports" | `src/Testigo/Data/support-contacts-v1.json`, or `Testigo:Data:SupportContacts` |
| Q5 | The immediate-danger rules: which words and phrases must always raise the flag, and the protocol staff follow | FR-ANA-6 | Rule set v1: self-harm, threats, violence, a child at risk | `src/Testigo/Data/danger-rules-v1.json`, or `Testigo:Data:DangerRules` |
| Q6 | The quick-exit destination: which neutral page, and whether Escape twice is right | FR-INT-5 | A weather search | `Reporter:QuickExitUrl` (`TESTIGO_QUICK_EXIT_URL` in compose) |
| Q7 | Who verifies a case: must it be a Reviewer? | FR-CASE-4 | Yes, a Reviewer, with at least one confirmed classification | `Testigo:Staff:ReviewerVerifies` |
| Q8 | Retention periods per data type (reports, contact details, redaction maps, audit) | FR-ADM-3 | Not built in Phase 0: nothing is deleted automatically | Phase 2 (`RetentionRule`) |
| Q9 | Which organization receives public reports on a server with several | FR-REG-1 | The default organization | `Testigo:Intake:Organization` |
| Q10 | Submissions allowed per client and per minute before the form refuses | NFR-SEC-3 | 5 per client per 10 minutes (reporter app); 120 intake requests per minute in total (API, 4 × `SubmissionsPerMinute`) | `Reporter:SubmissionsPerClient`, `Reporter:SubmissionWindowMinutes`, `Testigo:Intake:SubmissionsPerMinute` |
| Q11 | Two-factor sign-in for staff in the demo | NFR-SEC-8 | Required (`TESTIGO_REQUIRE_2FA=true`); the demo admin enrols under Ajustes → Cuenta first | `Testigo:Staff:RequireTwoFactor` |
| Q12 | Mapping to the AI Incident Database, OECD AI Incidents Monitor and AIAAIC: confirm their current schemas before building the export | §4.2 | Not built | Phase 3, a mapping data file |
| Q13 | Legal basis for each kind of processing under Chile's Law 19.628 and Law 21.719, and the 2021 neurorights reform | NFR-SEC-15 | A first draft for counsel in [`data-protection.md`](data-protection.md) | Documentation |
| Q14 | Which second country or partner for the regional pilot | §12 Q4 | Not started | — |
| Q15 | The English texts for reporters: a person must review them before real use (FR-INT-8), and the deploying organization decides whether English is offered at all | FR-INT-8 | A first draft, offered at `/en` beside Spanish; the §12 banner stays in Spanish with an English line under it | `src/Testigo.Reporter/Texts/en.json`, `translations.en` in the form file, English labels in the support-contacts file, `ValidationMessages.English`; `Reporter:Languages` to turn it off |
| Q16 | Who participates in co-design, how are they recruited and compensated, and what access/language support is needed? | G6, NFR-UX-3 | Proposed only; no contact or sessions completed | `community-participation.md`; $3,000 draft participation/accessibility envelope |
| Q17 | Who can pause a pilot, veto data collection/publication or resolve a disagreement? | G6, FR-INT-2, FR-PAT-5 | Proposed decision rights, not agreed | `community-participation.md` |
| Q18 | Who operates, maintains and funds the deployment, and what happens if participation or funding stops? | NFR-OPS-1, §12 | Discuss in the first stage, not after deployment | `community-participation.md` |
| Q19 | Which model providers and processing boundaries are acceptable? | NFR-AI-1, NFR-AI-2 | Mock by default; all supported providers configurable for synthetic tests; local-only enforcement is opt-in | `Testigo:LocalModelsOnly` (`TESTIGO_LOCAL_MODELS_ONLY`), `Llm`; `llm-settings.md` |
| Q20 | Are the synthetic labels and plain-language taxonomy terms appropriate? Distinguish treatment consent from data-collection consent (for example eval r11). | §4, §10, NFR-UX-3 | Author labels, not expert-validated ground truth | `evals/testigo/make_reports.py`, `src/Testigo/Data/taxonomy-v1.json` |

## Questions that aren't VALIDATE items but need someone's answer

- **Hosted demo or recording (§12, optional).** A hosted demo needs a server, a daily reset and
  submissions discarded; a 2–3 minute recording needs none of that. Not started.
- **Publication.** The repository is published only after a secret scan of the code and its
  history (NFR-OSS-2).
- **Private security/conduct contact (D8, NFR-SEC-14).** Pending by the author's choice; must be
  configured and tested before public release. No operational reporting channel is claimed.
