# Security

Testigo is meant to hold reports of human-rights harms, so security problems put people at risk.
**This is a Phase 0 prototype with synthetic data. Don't use it for real reports.**

## Reporting a vulnerability

**Private contact not configured yet (D8, NFR-SEC-14).** The author has left this pending.
There is currently no documented private reporting service or response-time commitment.
Do not put vulnerability details, secrets or personal data in public issues, pull requests or chats.
A public request to establish a private channel should contain no sensitive details.

Before public release, maintainers must supply and test a maintained private address or disclosure
URL, name who can access reports, state realistic acknowledgment times and provide an alternative
for complaints involving a maintainer. Never invent an address that appears operational.
Once that channel exists, reports should include reproduction steps using synthetic data.
Test only servers you operate or have permission to test.

**Resumen:** aún no hay un contacto privado configurado. No publiques vulnerabilidades,
credenciales ni datos personales. Este requisito sigue pendiente antes de publicar el proyecto.

## Rules this repository follows

- No secrets in the repository. Real credentials live only in `.env` (ignored by git) or in the
  deployment's secret settings. `.env.example` and `appsettings.json` hold local demo defaults only.
- The history is scanned for secrets before anything is published (NFR-OSS-2).
- Synthetic data only, in tests, seeds, demos and evals (requirements §10.4).
- No third-party requests from reporter pages (NFR-SEC-2) and a strict Content Security Policy
  (NFR-SEC-6).
- Logs never contain report content, contact details, case codes or reporter IP addresses
  (NFR-OPS-4, NFR-SEC-3).
- Agents have no tool that publishes, sends, deletes or changes permissions (NFR-SEC-12).

The threat model is in [docs/threat-model.md](docs/threat-model.md).

## Before a server is reachable by anyone else

The defaults in `docker-compose.yml` and `.env.example` are for one local machine:

- use `docker-compose.prod.yml` (TLS through Caddy, no published API or database ports);
- set `LOCAL_ADMIN_USERNAME=` (empty) to turn off the built-in `admin` / `admin` account. The
  production overlay has none unless `.env` names one, and the API refuses to start behind a proxy
  with a local admin password under 10 characters;
- set strong values for `POSTGRES_PASSWORD` and `SECRETS_MASTER_KEY`, and back up the master key;
- set `PLATFORM_ADMIN_EMAIL` to the operators' emails and create the operator's account first
  (it takes `SETUP_TOKEN`, generated and logged by the API when empty);
- choose the model policy explicitly: `TESTIGO_LOCAL_MODELS_ONLY=true` enforces Mock or local
  model endpoints (including compatible APIs); otherwise hosted providers can receive synthetic text with identifiers
  missed by rules (`docs/llm-settings.md`). Real-data cloud use is not validated;
- establish the private reporting contact above. A public interactive demo also needs tested
  synthetic-only intake/discard and reset behavior; the current local demo does not delete automatically.
