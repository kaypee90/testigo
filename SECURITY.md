# Security

Testigo is meant to hold reports of human-rights harms, so security problems put people at risk.
**This is a Phase 0 prototype with synthetic data. Don't use it for real reports.**

## Reporting a vulnerability

Report anything that looks like a security problem to the maintainers privately, never in a
public issue, pull request or chat channel. Include what you found, how to reproduce it, and what
an attacker could do with it. Please don't test against any server you don't run yourself, and
never submit real personal data to a demo.

If you think a secret (an API key, a password, `SECRETS_MASTER_KEY`, a case-data key) has leaked,
say so right away so it can be rotated, even if you aren't sure.

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
- keep `LLM_ALLOW_PRIVATE_BASE_URLS=false` unless everyone who can sign in is trusted with the
  server's internal network.
