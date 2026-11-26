# Contributing to Testigo

Testigo is a Phase 0 prototype: an open platform for reporting and analyzing harms from AI and
neurotechnology in Latin America. Contributions
in Spanish, Portuguese or English are welcome.

## Before you start

- Read the [README](README.md) and [`docs/`](docs/README.md) for what Testigo does and why.
- Open an issue before large changes. Items marked **DECISION** need the maintainers' choice
  first; items marked **VALIDATE** are questions for the deploying organization and belong in
  configuration, not code ([`docs/open-questions.md`](docs/open-questions.md)).
- Do not post security details in public issues. The private contact is still pending
  ([`SECURITY.md`](SECURITY.md)); a public request for a contact must contain no sensitive details.

Adapting Testigo (a taxonomy, a form, a country, a language, a model provider, a new analysis)
is described in [`docs/extending.md`](docs/extending.md).

## Aportes sin código / non-code contributions

Puedes proponer mejoras de lenguaje, revisar accesibilidad, comentar escenarios ficticios o
explicar qué preguntas debería resolver el proyecto. No hace falta programar ni compartir una
experiencia personal. Usa español, portugués o inglés. La participación con comunidades todavía
es una propuesta: [`docs/community-participation.md`](docs/community-participation.md).

Language review, accessibility feedback and critiques of fictional scenarios are welcome without
code changes. Maintainers should record disagreements and explain scope decisions; a contribution
does not imply endorsement or agreement to deploy.

## Rules that are never relaxed

- **Synthetic data only**, in tests, seeds, demos and evals. Invented names and companies;
  Chilean RUTs that **fail** the check digit (a test scans the repository for valid ones). Never
  real reports, real people's data or scraped personal data.
- **Security before convenience.** If a requirement in §6 (NFR-SEC) gets in your way, ask.
- **The prototype banner on every page** of both apps, until the project leaves Phase 0.
- **No third-party requests from reporter pages** and no inline scripts (NFR-SEC-2, NFR-SEC-6).
- **Logs never contain** report content, contact details, case codes or reporter IP addresses.
- **Agents suggest, people decide.** No agent tool publishes, sends, deletes or changes
  permissions. Report text is untrusted input.
- **No secrets** in the repository or its history.

## Working on the code

```bash
dotnet test src/AgentRuntime.Tests
dotnet test src/Testigo.Tests
TESTCONTAINERS_RYUK_DISABLED=true dotnet test src/AgentRuntime.IntegrationTests   # needs Docker or Podman
dotnet run --project src/Testigo.Evals                                            # the triage eval
(cd src/web && npx tsc --noEmit && npm run lint)
```

- Every change keeps the build, the tests and the eval targets green; new behavior comes with
  tests. Use the Mock LLM provider in tests; real models only in dedicated eval runs.
- Reference requirement IDs (`FR-INT-3`, `NFR-SEC-2`) in commits, tests and pull requests.
- The runtime enforces limits, not the model: budgets, permissions and validation are checked in
  code, and nothing a model returns can widen them. Structured tool calls only; no unbounded loops
  or retries.
- The staff dashboard uses a recent Next.js: read the guides in
  `src/web/node_modules/next/dist/docs/` before changing it.
- Reporter-facing text is plain Spanish, calm and trauma-informed (NFR-UX-3, NFR-UX-4). Keep it in
  `src/Testigo.Reporter/Texts/es.json`.

## License

By contributing you agree that your contribution is licensed under the [MPL-2.0](LICENSE).
