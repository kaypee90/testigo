<p align="center"><img src="docs/assets/testigo-logo.svg" width="80" height="80" alt=""></p>

# Testigo

> **Prototipo con datos ficticios. No es un canal oficial de denuncia. No envíe información real.**
>
> *Prototype with synthetic data. Not an official reporting channel. Do not submit real information.*

[Español](#español) · [English](#english)

---

## Español

**Testigo** es una plataforma abierta para reportar y analizar daños causados por la inteligencia
artificial y la neurotecnología en América Latina, empezando por Chile.

### Qué hace

- **Reporte seguro y anónimo.** Un formulario público en español (y en inglés en `/en`, un borrador
  por revisar), separado del panel interno, que
  funciona sin JavaScript, en teléfonos antiguos y con conexiones lentas. No carga nada de
  terceros. Quien reporta recibe un código de caso y una frase de acceso para hacer seguimiento,
  sin cuenta ni correo.
- **Evidencia protegida.** El contenido de los reportes se cifra campo por campo antes de llegar a
  la base de datos. Cada lectura queda en un registro de auditoría encadenado.
- **Análisis asistido, decisiones humanas.** Un modelo de IA propone una clasificación (tecnología,
  tipo de daño, derechos afectados), marcan urgencias y sugieren reportes relacionados. Cada
  sugerencia debe citar textualmente el reporte, y el sistema verifica la cita en código antes de
  mostrarla. Una persona confirma, corrige o descarta cada sugerencia. El modelo no tiene
  herramientas para publicar, enviar mensajes, borrar ni cambiar permisos.
- **Datos personales fuera del modelo.** Antes de que un modelo lea un reporte, se reemplazan los
  datos identificables (RUT, teléfonos, correos, direcciones, nombres) por marcadores.
- **Autoalojable.** Un solo `docker compose up`, con modelos locales (Ollama) como opción por
  defecto y sin depender de una nube ni de un proveedor de IA.

### Probarlo

Requiere Docker (o Podman con `docker compose`).

```bash
docker compose up --build
```

- Formulario público de reporte: http://localhost:3102 (en inglés: http://localhost:3102/en)
- Panel del equipo: http://localhost:3101 (usuario `admin`, contraseña `admin`, solo en la demo).
  Los casos están en **Casos**. Por defecto los casos exigen verificación en dos pasos: actívala en
  Settings → Account, o levanta la demo con `TESTIGO_REQUIRE_2FA=false` solo en tu máquina.
- API: http://localhost:5180

La demo trae 14 reportes ficticios ya analizados. El modelo por defecto es `Mock`: determinista,
sin clave y sin enviar datos a nadie. Todos los datos de la demo son ficticios.

### Evaluación

```bash
dotnet run --project src/Testigo.Evals
```

Sobre 30 reportes sintéticos (`evals/testigo`), con el modelo Mock: validez de citas 100 %
(151/151), fuga de datos personales en la copia anonimizada 0 de 11 identificadores, detección de
peligro inmediato 100 %, resultados repetibles 30/30, 3 de 3 intentos de inyección sin efecto en
el comportamiento. Los números completos, incluidos los débiles (clasificación F1 0,68, duplicados
no detectados), están más abajo en inglés.

### Adaptarlo

La taxonomía, el formulario, las reglas de peligro y los contactos de apoyo son archivos de datos
versionados que se reemplazan sin tocar código. Cómo agregar un país, un idioma, un proveedor de
modelos o un análisis: [`docs/extending.md`](docs/extending.md) (en inglés).

### Estado

Fase 0 (prototipo). Las preguntas abiertas para la organización anfitriona están en
[`docs/open-questions.md`](docs/open-questions.md).

---

## English

**Testigo** ("witness") is an open platform for reporting and analyzing harms from artificial
intelligence and neurotechnology in Latin America, starting in Chile.

### The problem

AI systems and neurotechnology (consumer EEG headsets, neural-data apps, emotion recognition,
automated decisions) already affect people's rights in the region, but there is no trusted, safe
way for people to report these incidents, or for advocates to analyze them as evidence. The
region's cases are largely missing from global incident trackers.

### What is technically different

- **Grounded triage.** Agents classify each report against a versioned, editable harm taxonomy
  (with neural data and mental integrity as first-class dimensions). Every suggestion must carry
  an exact quote from the report; the runtime checks the quote against the original text and
  drops any suggestion it can't ground. Citation validity is measured on every eval run.
- **Redaction before the model.** Identifying data, including Chilean RUTs validated by their check
  digit, is replaced with typed placeholders before any model sees a report. Redaction leakage is
  measured on every eval run.
- **The code enforces the limits, not the model.** Triage is a bounded pipeline: one model call
  per report, through a single structured tool, with every result validated in code. When a call
  fails or a check doesn't pass, the case goes to a person as "analysis incomplete"; nothing
  retries in a loop. The model has no tool that publishes, sends, deletes or changes permissions.
- **An intake built for people at risk.** Anonymous by default, Spanish first, server-rendered,
  works with JavaScript off, a strict Content Security Policy, a quick-exit button, and no
  third-party requests of any kind.
- **Tamper-evident evidence.** Field-level envelope encryption with key rotation, and a
  hash-chained audit log of every staff read and decision.

### Try it

Needs Docker (or Podman with `docker compose`).

```bash
docker compose up --build
```

- Public report form: http://localhost:3102 (Spanish; English at http://localhost:3102/en, a draft
  awaiting human review)
- Staff dashboard: http://localhost:3101 (`admin` / `admin`, demo only). Cases are under
  **Casos**. Case data needs two-factor sign-in by default: turn it on under Settings → Account,
  or start the demo with `TESTIGO_REQUIRE_2FA=false` on your own machine only.
- API: http://localhost:5180

The demo ships 14 synthetic reports, already analyzed. The default model is `Mock`:
deterministic, no key, and nothing leaves the machine. To use a local model, see
`COMPOSE_PROFILES=ollama` in [`.env.example`](.env.example).

### Tests and evaluation

```bash
dotnet test src/AgentRuntime.Tests
dotnet test src/Testigo.Tests
TESTCONTAINERS_RYUK_DISABLED=true dotnet test src/AgentRuntime.IntegrationTests   # needs Docker/Podman
```

The triage eval runs the real pipeline over the 30 synthetic reports in
[`evals/testigo`](evals/testigo) and fails when a target of requirements §10.2 is missed:

```bash
dotnet run --project src/Testigo.Evals                       # Mock provider, as in CI
dotnet run --project src/Testigo.Evals -- --provider Ollama --model qwen3:4b-instruct
```

Results with the Mock provider (2026-11-26):

| Metric | Target (§10.2) | Result |
|---|---|---|
| Citation validity (every quote is in the report) | 100% | **100%** (151/151) |
| Identifiers leaked into redacted copies | 0 | **0** of 11 |
| Immediate-danger recall | ≥ 98% | **100%** (precision 100%) |
| Determinism (same input, same result) | 100% | **30/30** |
| Prompt injections that change agent behavior | 0 | **0** of 3 (one injected sentence still added a term through a keyword cue) |
| Classification against the labels, per dimension | report it | F1 0.59–0.87, 0.68 overall (precision 87%, recall 56%) |
| Duplicate detection | report it | precision 100%, **recall 0%**: both near-duplicate pairs scored below the threshold |

The Mock is a keyword heuristic that stands in for a model so the pipeline can be tested; its
classification numbers say little about a real model. The first eval run found a real redaction
gap ("Mi pasaporte es AB1234567"), now fixed. Duplicate detection is a bag-of-words baseline until
hybrid search with pgvector (FR-ANA-5) replaces it.

### For reviewers: what this prototype shows

- **A technical core.** A reporting pipeline where the safety properties are enforced in code, not
  asked of a model: field-level envelope encryption, a separate reporter origin with no
  third-party requests, a single-tool bounded model call whose every quote and term is checked in
  code, and a hash-chained audit trail of every staff read.
- **An emerging-technology problem.** Neural data and mental integrity are first-class dimensions
  of the harm taxonomy, alongside AI harms such as automated decisions and emotion recognition;
  Chile protects mental integrity in its constitution since 2021, and there is no safe channel
  for people to report these harms.
- **What is new.** Grounded, redacted-before-the-model triage of human-rights reports, measured on
  every commit (citation validity and redaction leakage are CI gates), built for people at risk:
  anonymous, no JavaScript needed, works on a cheap phone, in Spanish and (as a draft) English.
- **Evidence it can be built.** It runs today with `docker compose up`: the report form, encrypted
  storage, automatic triage, the analyst queue and case view. 144 domain and reporter tests, the
  platform's unit and integration suites, and the eval above.
- **Benefit to communities, with the host.** Everything that depends on the people it serves (the
  taxonomy, the form wording, support contacts, danger rules, retention) is data or configuration,
  listed as open questions to settle with the organization that deploys it
  ([`docs/open-questions.md`](docs/open-questions.md)).

### Repository layout

```
src/Testigo                  case domain: reports, consent, taxonomy, redaction, encryption, grounding, triage
src/Testigo.Infrastructure   EF Core entities, migrations and stores for case data
src/Testigo.Reporter         the public reporter app (separate origin, no JavaScript needed)
src/Testigo.Evals            the triage eval (citation validity, leakage and the other §10.2 targets)
src/AgentRuntime             model-provider interface and the hash-chained audit log
src/AgentRuntime.Infrastructure
                             model providers, staff accounts, two-factor sign-in, secrets
src/AgentRuntime.Api         the staff and intake API
src/web                      the staff dashboard (Next.js)
evals/testigo                the synthetic eval set
docs/                        requirements, decisions, threat model, data protection, open questions,
                             extending Testigo, dependency licenses
```

### Extending it

Testigo is built to be adapted. The taxonomy, the report form, the danger rules and the support
contacts are versioned data files a deployment can replace without code; model providers plug in
behind one interface; new analyses follow the triage pattern. See
[`docs/extending.md`](docs/extending.md).

### Principles

- Synthetic data only, everywhere: tests, seeds, demos, evals.
- Agents suggest, people decide.
- Logs never contain report content, contact details, case codes or reporter IP addresses.
- No billing, ads, analytics or data sales.

### Status

Phase 0 (prototype). Open questions for the host are in
[`docs/open-questions.md`](docs/open-questions.md), the threat model in
[`docs/threat-model.md`](docs/threat-model.md), and the security policy in
[`SECURITY.md`](SECURITY.md). Contributions: [`CONTRIBUTING.md`](CONTRIBUTING.md) and
[`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md).

### License and credits

[MPL-2.0](LICENSE). Testigo is built on [Aktor](NOTICE), an actor-based multi-agent runtime by the
same author, relicensed here under MPL-2.0. See [`NOTICE`](NOTICE).
