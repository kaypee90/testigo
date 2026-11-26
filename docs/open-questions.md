# Open questions

These items are assumptions about what a deploying organization needs. No organization has reviewed this
project; nothing here has been agreed with them. Each item is built so it can change without code:
a data file or a setting. The "Where" column says which.

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
| Q11 | Two-factor sign-in for staff in the demo | NFR-SEC-8 | Required (`TESTIGO_REQUIRE_2FA=true`); the demo admin enrols under Settings → Account first | `Testigo:Staff:RequireTwoFactor` |
| Q12 | Mapping to the AI Incident Database, OECD AI Incidents Monitor and AIAAIC: confirm their current schemas before building the export | §4.2 | Not built | Phase 3, a mapping data file |
| Q13 | Legal basis for each kind of processing under Chile's Law 19.628 and Law 21.719, and the 2021 neurorights reform | NFR-SEC-15 | A first draft for counsel in [`data-protection.md`](data-protection.md) | Documentation |
| Q14 | Which second country or partner for the regional pilot | §12 Q4 | Not started | — |
| Q15 | The English texts for reporters: a person must review them before real use (FR-INT-8), and the deploying organization decides whether English is offered at all | FR-INT-8 | A first draft, offered at `/en` beside Spanish; the §12 banner stays in Spanish with an English line under it | `src/Testigo.Reporter/Texts/en.json`, `translations.en` in the form file, English labels in the support-contacts file, `ValidationMessages.English`; `Reporter:Languages` to turn it off |

## Questions that aren't VALIDATE items but need someone's answer

- **Hosted demo or recording (§12, optional).** A hosted demo needs a server, a daily reset and
  submissions discarded; a 2–3 minute recording needs none of that. Not started.
- **Publication.** The repository is published only after a secret scan of the code and its
  history (NFR-OSS-2).
