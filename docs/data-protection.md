# Data protection (draft for legal review)

NFR-SEC-15 asks to record the legal basis for each kind of processing. **This is not legal advice
and has not been reviewed by a lawyer.** It describes what the prototype
processes and proposes a basis for each, so counsel can review it (open question Q13 in
[`open-questions.md`](open-questions.md)).

The Phase 0 prototype processes **synthetic data only**. The banner on every page says so, and no
real report may be submitted to it.

## Laws to review (Chile first)

- Law 19.628 on the protection of private life, and its reform by Law 21.719 (confirm the
  in-force dates of each part, and the role of the new data protection agency).
- The 2021 constitutional reform protecting mental integrity and brain activity and information
  (Article 19 No. 1), and the case law on neural data, including Chile's Supreme
  Court rulings.
- For partner countries later: Brazil's LGPD, Argentina's Law 25.326, Mexico's data protection
  law.

Report text often holds **sensitive data**: health, neural data, biometrics, sometimes data about
children or third parties. Each processing below should be checked against the rules for
sensitive data.

## Processing activities

| # | What | Data | Proposed basis (to confirm) | Who sees it | Kept |
|---|---|---|---|---|---|
| P1 | Receiving and storing a report | Free text, form answers, approximate place and date | Explicit consent of the reporter, given on the form (consent `processing`, required) with a versioned notice | Analysts and Reviewers of the receiving organization | Until retention rules are agreed (Q8); none are automatic in Phase 0 |
| P2 | Contacting the reporter | Email, phone or Signal handle, only if given | Separate, optional consent (`contact`); stored under a separate key | Staff who reveal it, each reveal recorded with a reason | As P1, or until the reporter withdraws |
| P3 | Automatic first-pass analysis (redaction, classification, danger flag, related reports) | Report text | Part of the purpose consented to in P1, explained in the notice; done on the organization's own server with a local model by default (NFR-AI-1) | No person; results are suggestions for staff | As P1 |
| P4 | Sending report text to a hosted model (optional, off by default) | Redacted text only (NFR-AI-2) | Needs its own analysis: transfer to a processor, possibly abroad | The model provider | Per the provider's terms |
| P5 | Publishing anonymized, verified incidents and statistics (Phase 3) | Anonymized incident data | Separate, optional consent (`publication`) | Public | Indefinitely (open data) |
| P6 | Audit log of staff actions | Staff identity, time, action, reason; no report content | Legitimate interest and legal obligations of the organization (accountability, chain of custody) | Reviewers, operators | Per the organization's policy |
| P7 | Staff accounts | Name, email, password hash, 2FA secret, sessions | Employment or volunteer relationship | Operators | While the account exists |
| P8 | Rate limiting on the public form | A salted hash of the client address, in memory only, salt rotated hourly | Legitimate interest (protecting the service); never stored or logged | No one | Minutes, in memory |

## What the prototype does to support these

- Consents are recorded with the notice version and time; contact and publication consents are
  separate and optional (FR-INT-3).
- Report content, contact details, redaction maps and suggestions are encrypted at the field level,
  with contact details under their own key (NFR-SEC-4).
- No reporter IP address or device fingerprint is stored (NFR-SEC-3).
- Every read of a case is recorded (NFR-SEC-10).
- Not in Phase 0: withdrawal and deletion by the reporter (FR-CASE-9), retention jobs, data
  export for the reporter, publication.

## Questions for counsel

1. Is explicit consent the right basis for P1–P3, given that reports describe third parties
   (companies, sometimes named people)? What must the notice say?
2. Reports about someone else (a relative, a child), and assisted intake by an advocate: whose
   consent, and how is it recorded?
3. Neural data under the 2021 reform: does it need protections beyond sensitive personal data?
4. Retention periods and what "deletion" must cover (backups, audit log, published data).
5. Using a hosted model on redacted text (P4): is it a transfer abroad, and what contract terms
   are needed?
6. Requests from authorities or the reported party for reports or reporter identity: what the
   organization must and must not hand over.
