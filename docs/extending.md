# Extending Testigo

Testigo is meant to be adapted: by organizations in Latin America that deploy it, and by contributors adding countries, languages, model providers and analyses.
This guide shows where each kind of change goes, and the rules each one must keep.

The rules in [`../CONTRIBUTING.md`](../CONTRIBUTING.md) (synthetic data only,
security before convenience, the prototype banner, no third-party requests from reporter pages)
apply to every extension.

## The shape of the code

```
src/Testigo                  the domain: reports, cases, taxonomy, form, redaction, encryption,
                             danger rules, triage. No database, web or provider code.
src/Testigo/Data             the data files (taxonomy, form, danger rules, support contacts)
src/Testigo.Infrastructure   EF Core storage, the triage worker, the Mock triage behavior, demo seed
src/Testigo.Reporter         the public report form (Razor Pages, no JavaScript needed, own origin)
src/AgentRuntime             provider-agnostic model calls (ILLMProvider) and the audit log
src/AgentRuntime.Infrastructure
                             the model providers, staff accounts, two-factor sign-in, secrets
src/AgentRuntime.Api         the staff API and the intake API
src/web                      the staff dashboard (Next.js)
src/Testigo.Evals            the triage evaluation (citation validity and leakage)
evals/testigo                the synthetic evaluation set
```

The dependency direction is one way: `Testigo` knows nothing about the others, and the API wires
everything together in `Program.cs`.

## Things that are data, not code

Most adaptations need no code at all. Each file below is embedded in the build as a starting
version and can be replaced at deployment time by pointing a setting at your own file. Every file
carries a `version`, which is recorded with each report and each analysis, so you can always tell
which version produced what.

| What | Starting file | Setting that replaces it |
| --- | --- | --- |
| Harm taxonomy (§4) | `src/Testigo/Data/taxonomy-v1.json` | `Testigo:Data:Taxonomy` |
| Report form (FR-INT-1) | `src/Testigo/Data/form-v1.json` | `Testigo:Data:Form` |
| Immediate-danger rules (FR-ANA-6) | `src/Testigo/Data/danger-rules-v1.json` | `Testigo:Data:DangerRules` |
| Emergency and support contacts (FR-ADM-6) | `src/Testigo/Data/support-contacts-v1.json` | `Testigo:Data:SupportContacts` |
| Reporter texts (FR-INT-8) | `src/Testigo.Reporter/Texts/es.json`, `en.json` | embedded; `Reporter:Languages` picks which are offered |
| Staff dashboard texts | `src/web/src/lib/i18n/es.ts`, `en.ts` | built in; each person picks a language in the dashboard |

With Docker Compose, mount your file into the `api` container and set the matching environment
variable, for example `Testigo__Data__Taxonomy=/config/taxonomy-v2.json`.

### Changing the taxonomy

Add or rename dimensions and terms in a copy of `taxonomy-v1.json`, and give it a new `version`.
Every term needs a stable `key` (it is stored with cases; never reuse a key for a different
meaning) and labels in `es`, `en` and `pt`. `mappings` hold the equivalent term in the external
trackers (§4.2); leave a value `null` until the mapping has been checked against that tracker's
current schema.

The form refers to taxonomy dimensions by key, so a form field with `"taxonomy": "technology"`
offers that dimension's terms. Loading fails at start-up if the form names a dimension the
taxonomy doesn't have.

### Changing the form

Fields have a `key`, a `kind` (`text`, `longtext`, `choice`, `multichoice`),
`required`, an optional `max_length`, and the `label` and `help` a reporter reads. Keep wording
plain and calm (NFR-UX-3, NFR-UX-4), and don't ask for anything you don't need: every field is
data you then have to protect. Changing `notice_version` means reporters see, and accept, a new
privacy notice.

### Adding a country

1. Add the country's emergency and support contacts to the support-contacts file. Each contact
   is a `label` with a `phone` and/or a `url`. Check every number with someone in that country;
   a wrong emergency number is worse than none.
2. If the country has an identity number that should be redacted, add a recognizer to
   `src/Testigo/Redaction/Redactor.cs` with tests in `RedactionTests.cs`. Test data must be
   invalid identifiers (for Chilean RUTs, ones that **fail** the check digit).
3. Add danger-rule patterns in the country's language or usage if they differ.

### Danger rules

Each rule has a `key`, a staff-facing `label` and a list of regular expressions. Matching ignores
case and accents, so write patterns without accents. Rules run on every report alongside the
model's own check; either one raises the flag, and the rules work even when no model is
configured. Add a synthetic example to `DangerDetectorTests` (in `TextAndDataTests.cs`) for each new pattern, and agree new
rules with a support professional.

### A new language for reporters

Spanish is the form's own language and lives at the root (`/`, `/reportar`). Every other
language lives under its code (`/en`, `/en/reportar`): the address carries the language, so
switching needs no cookie and no JavaScript. Each page links to itself in the other languages.
English is there as a worked example, and a draft until a person reviews it (open question Q15).
Offering that draft is a synthetic-prototype exception, not approval for use with real reporters.

To add Portuguese (or another language):

1. Copy `src/Testigo.Reporter/Texts/en.json` to `pt.json` and translate every key, including
   `language.name` (the language's name in that language, shown in the switch). Keep `banner` a
   faithful translation: the Spanish §12 banner is always shown above it.
2. Add a `pt` block under `translations` in the form file, with its own `notice_version`, and
   every field label and help, option, consent and contact method. Loading fails at start-up if
   anything is missing, so a page never switches language halfway down.
3. Add `pt` labels to every taxonomy term, and to each contact (`labels`) and `response_times` in
   the support-contacts file.
4. Add the error messages to `ValidationMessages` in `src/Testigo/Forms/ReportForm.cs`, and the
   personal-data names to `PersonalDataType` in `src/Testigo/Redaction/Redactor.cs`.
5. `Reporter:Languages` lists the languages offered (all with a texts file, by default). Turn a
   language on only after a person who speaks it has reviewed every text; machine-only
   translations are not accepted for reporter text.

The report records the language it was sent in (through its notice version), so staff can tell
which text a reporter agreed to. Tests check that every texts file has exactly the keys of
`es.json` (`ReporterAppTests`) and that each form translation is complete (`FormValidationTests`).

### A new language for the staff dashboard

The dashboard's texts are in `src/web/src/lib/i18n`: `es.ts` is the source, and every other
language is an object of the same shape (`Messages`). Each person picks a language in the account
menu or on the sign-in page; the choice is kept in a first-party cookie (`testigo_lang`, the code
only), so pages render in it from the server. The Spanish §12 banner is always shown, with the
translation of `banner` below it.

To add Portuguese:

1. Copy `en.ts` to `pt.ts`, type it as `Messages`, and translate every text, including
   `language.name` and `language.locale` (for dates, for example `pt-BR`).
2. Add `pt` to `LANGUAGES` in `languages.ts`. The switch lists it automatically.
3. Run `npm test` in `src/web`: it checks that every language has exactly the Spanish keys, no
   empty texts and a working locale. `npx tsc --noEmit` catches the same gaps while you type.

What comes from the server keeps the server's language: taxonomy labels, form questions, report
text, history reasons, audit summaries and API error messages. Translating those is the reporter
side's work above (taxonomy and form labels) or the API's.

## Things that are code

### A new model provider

Triage talks to models through one interface, `ILLMProvider`
(`src/AgentRuntime/LLM/ILLMProvider.cs`): a list of messages and tools in, text and structured
tool calls out. Domain code never sees a provider's own types.

1. Add `YourProvider : ILLMProvider` under `src/AgentRuntime.Infrastructure/Llm/`, taking an
   `HttpClient` and `IOptions<LlmOptions>`. Map tool calls to and from the provider's format;
   never parse free-form text to decide an action.
2. Register it in `AddLlmProvider` in
   `src/AgentRuntime.Infrastructure/ServiceCollectionExtensions.cs`, under a new `Llm:Provider`
   name. Update the recognized provider list in `CaseModelPolicy`; do not classify a new provider
   as local without tests of its endpoint and egress policy.
3. Add contract tests next to the existing ones in `src/AgentRuntime.Tests` (a fake
   `HttpMessageHandler`, no network).
4. Document it in [`llm-settings.md`](llm-settings.md), including where report text goes when
   the provider is used ([`data-protection.md`](data-protection.md)). Rules can miss identifiers
   in the intermediate copy sent to the provider; final-copy redaction occurs afterward.

### Scripting the Mock provider

The Mock provider (`Llm:Provider=Mock`, the default) makes the demo, the tests and CI run with
no model and no network. It answers through `IMockLlmBehavior` implementations, each
recognizing the requests it handles (usually by the tool offered). Triage's is
`src/Testigo.Infrastructure/Triage/MockTriageBehavior.cs`. A new analysis that calls a model
should come with its own behavior, registered with
`services.AddSingleton<IMockLlmBehavior, YourBehavior>()`, so its tests stay deterministic.

### A new analysis

Triage (`src/Testigo/Triage`) is the pattern to follow:

- **Redact before the model sees anything**, and give the model the report only as quoted,
  untrusted input (`TriagePrompt`).
- **One structured tool** for the answer, with no tool that publishes, sends, deletes or changes
  permissions (FR-ANA-8, NFR-SEC-12).
- **Ground every claim**: each suggestion carries a quote, and the code checks that the quote
  really appears in the report before showing it. Ungrounded suggestions are dropped.
- **Bounded work**: one attempt per report. When a budget or a check fails, the case is marked
  "analysis incomplete" for a person; there is no retry loop (NFR-AI-5).
- **People decide**: analyses produce suggestions that staff accept or reject, and every decision
  is recorded in the audit log.

Add synthetic cases to `evals/testigo` and make sure `dotnet run --project src/Testigo.Evals`
still meets its targets.

### A new staff page

The dashboard's menu is the `NAV` list in `src/web/src/components/shell/AppShell.tsx`. Pages
live under `src/web/src/app`; API calls go through `apiFetch` (`src/web/src/lib/api.ts` for
accounts, `src/web/src/lib/testigo.ts` for cases). Read the guides in
`src/web/node_modules/next/dist/docs/` before writing frontend code: this Next.js version differs
from older ones. Use the existing colour tokens
(`brand-*` and the neutrals in `src/web/src/app/globals.css`) so the page works in both light and
dark mode, and every page keeps the prototype banner.

On the API side, a new endpoint checks the caller's staff role in code (`StaffAccess`), reads case
content only through the services that record each read in the audit log, and never logs report
content, contact details, case codes or IP addresses.

### Storage changes

Case data lives in its own schema (`TestigoDbContext`, `src/Testigo.Infrastructure/Persistence`).
Report content and contact details are encrypted per field (`IFieldCipher`); a new sensitive
field must be too. Add a migration with:

```bash
dotnet ef migrations add YourChange --project src/Testigo.Infrastructure --startup-project src/AgentRuntime.Api --context TestigoDbContext
```

## Testing an extension

```bash
dotnet test src/AgentRuntime.Tests
dotnet test src/Testigo.Tests
TESTCONTAINERS_RYUK_DISABLED=true dotnet test src/AgentRuntime.IntegrationTests   # needs Docker or Podman
dotnet run --project src/Testigo.Evals                                            # the triage eval
(cd src/web && npx tsc --noEmit && npm run lint)
```

Use the Mock provider in tests and synthetic data everywhere. Reference the requirement IDs your
change touches (`FR-INT-3`, `NFR-SEC-2`) in commits, tests and the pull request.

## Sharing an adaptation

If you adapt Testigo for your own organization or country, consider contributing the
data files back (a taxonomy translation, a country's support contacts, danger-rule patterns for
a language), so others can start from them. Open an issue to discuss it first.
