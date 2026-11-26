<p align="center"><img src="docs/assets/testigo-logo.svg" width="80" height="80" alt=""></p>

# Testigo

> **Prototipo con datos ficticios. No es un canal oficial de denuncia. No envíe información real.**
>
> *Prototype with synthetic data. Not an official reporting channel. Do not submit real information.*

[Español](#español) · [English](#english)

---

## Español

**Testigo** es un prototipo independiente de código abierto para explorar cómo documentar posibles
daños asociados a la IA y la neurotecnología en América Latina, empezando por escenarios de Chile.
No se ha contactado a ninguna organización sobre esta propuesta ni se ha acordado un despliegue.
El alcance y la gobernanza se definirían con participantes y una posible organización colaboradora.

### Qué hace

- **Formulario sin cuenta, con medidas de protección.** En español y en inglés (`/en`, borrador
  por revisar), separado del panel interno y renderizado en el servidor. Funciona sin JavaScript;
  la accesibilidad, los teléfonos antiguos y las conexiones lentas requieren validación adicional.
  Usa recursos propios. Genera un código y una frase para mostrar el flujo previsto, pero el
  seguimiento aún no está disponible. No garantiza anonimato.
- **Evidencia protegida.** El contenido de los reportes se cifra campo por campo antes de llegar a
  la base de datos. Cada lectura queda en un registro de auditoría encadenado.
- **Análisis asistido, decisiones humanas.** Un modelo de IA propone una clasificación (tecnología,
  tipo de daño, derechos afectados), marca urgencias y sugiere reportes relacionados. Cada
  sugerencia debe citar textualmente el reporte, y el sistema verifica la cita en código antes de
  mostrarla. Una persona confirma, corrige o descarta cada sugerencia. El modelo no tiene
  herramientas para publicar, enviar mensajes, borrar ni cambiar permisos.
- **Sustitución de identificadores detectados.** Las reglas actúan antes del modelo y una segunda
  pasada asistida actualiza la copia después. Ambas pueden omitir datos. Se pueden configurar
  proveedores locales o de nube para pruebas sintéticas; `TESTIGO_LOCAL_MODELS_ONLY=true`
  restringe el servidor a Mock y endpoints locales (Ollama o APIs compatibles). Mock sigue siendo el valor por defecto.
- **Autoalojable.** `docker compose up` usa Mock por defecto; Ollama local es una opción.
  No se necesita un servicio de IA de pago.

### Probarlo

Requiere Docker (o Podman con `docker compose`).

```bash
docker compose up --build
```

- Formulario público de reporte: http://localhost:3102 (en inglés: http://localhost:3102/en)
- Panel del equipo: http://localhost:3101 (usuario `admin`, contraseña `admin`, solo en la demo).
  Los casos están en **Casos**. Por defecto los casos exigen verificación en dos pasos: actívala en
  Ajustes → Cuenta, o levanta la demo con `TESTIGO_REQUIRE_2FA=false` solo en tu máquina.
- API: http://localhost:5180

La demo trae 14 reportes ficticios ya analizados. El modelo por defecto es `Mock`: determinista,
sin clave y sin enviar datos a un proveedor. Los envíos persisten; no hay borrado automático.
Todos los datos deben ser ficticios. Una demo pública interactiva requiere medidas adicionales
de descarte/restablecimiento; por ahora puede mostrarse mediante una grabación.

### Evaluación

```bash
dotnet run --project src/Testigo.Evals
```

Sobre 30 reportes sintéticos (`evals/testigo`), con el modelo Mock: validez de citas 100 %
(151/151), 0 de 11 identificadores conocidos en la **copia final**, peligro inmediato detectado
en 3 de 3 casos etiquetados y resultados repetibles 30/30. Eso no demuestra anonimato: el
evaluador también mide los identificadores en las solicitudes al modelo, antes de la segunda
pasada. En 3 pruebas de inyección no hubo acciones externas, pero una alteró los términos
sugeridos. Los resultados y límites están más abajo en inglés.

### Adaptarlo

La taxonomía, el formulario, las reglas de peligro y los contactos de apoyo son archivos de datos
versionados que se reemplazan sin tocar código. Cómo agregar un país, un idioma, un proveedor de
modelos o un análisis: [`docs/extending.md`](docs/extending.md) (en inglés).

### Estado

Fase 0 (prototipo). La [participación propuesta](docs/community-participation.md) incluye decidir
si la herramienta es útil y qué no debe recogerse o publicarse. No se ha realizado co-diseño.
Las preguntas abiertas están en [`docs/open-questions.md`](docs/open-questions.md).

---

## English

**Testigo** ("witness") is an independent, open-source prototype exploring documentation of
possible AI and neurotechnology harms in Latin America, starting with Chilean scenarios.
No organization has been contacted about this proposal, endorsed it or agreed to deploy it.
Scope and governance would be developed with participants and a potential deploying organization.

### The problem

Testigo explores whether a locally governed, Spanish-first reporting workflow could complement
existing advocacy, case-management and incident-tracking efforts. Unmet needs, existing options
and conditions for safe use still need validation with affected people and local organizations.
It processes narratives, not neural measurements, and does not establish causation or legal liability.

### What is technically different

- **Grounded triage.** Agents classify each report against a versioned, editable harm taxonomy
  (with neural data and mental integrity as first-class dimensions). Every suggestion must carry
  an exact quote from the report; the runtime checks the quote against the original text and
  drops any suggestion it can't ground. Citation validity is measured on every eval run.
- **Two-stage redaction, with limits.** Rules replace detected identifiers before inference;
  model-assisted redaction updates the final copy afterward. Either can miss identifiers or
  identifying context. Actual model-request exposure and final-copy leakage are measured separately.
  All supported providers remain configurable for synthetic testing. Opt in to
  `TESTIGO_LOCAL_MODELS_ONLY=true` to enforce Mock/operator-controlled local endpoints, including
  Ollama and local OpenAI-compatible servers.
  Mock remains the default; see [`docs/llm-settings.md`](docs/llm-settings.md).
- **The code enforces the limits, not the model.** Triage is a bounded pipeline: one model call
  per report, through a single structured tool, with every result validated in code. When a call
  fails or a check doesn't pass, the case goes to a person as "analysis incomplete"; nothing
  retries in a loop. The model has no tool that publishes, sends, deletes or changes permissions.
- **A privacy-conscious intake prototype.** No account required, Spanish first, server-rendered,
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
  **Casos**. Case data needs two-factor sign-in by default: turn it on under Ajustes → Cuenta,
  or start the demo with `TESTIGO_REQUIRE_2FA=false` on your own machine only.
- API: http://localhost:5180

The demo ships 14 synthetic reports, already analyzed. The default model is `Mock`:
deterministic, no key, and nothing leaves the machine. To use a local model, see
`COMPOSE_PROFILES=ollama` in [`.env.example`](.env.example).
Submissions persist; automatic deletion and reporter withdrawal are not implemented. The current
setup is a local synthetic demo, not a real reporting service.

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
| Known identifiers remaining in the final analysis copy | 0 | **0** of 11 |
| Known identifiers in actual model requests | report separately | **1 of 11** across 63 Mock requests; final-copy zero does not imply request zero |
| Immediate-danger recall | ≥ 98% | **100%** on 3 labeled danger cases (precision 100%) |
| Determinism (same input, same result) | 100% | **30/30** |
| Prompt injections that change agent behavior | 0 | **0** of 3 (one injected sentence still added a term through a keyword cue) |
| Classification against the labels, per dimension | report it | F1 0.59–0.87, 0.68 overall (precision 87%, recall 56%) |
| Duplicate detection | report it | precision 100%, **recall 0%**: both near-duplicate pairs scored below the threshold |

The Mock is a keyword heuristic that stands in for a model so the pipeline can be tested; its
classification numbers say little about a real model. The first eval run found a real redaction
gap ("Mi pasaporte es AB1234567"), now fixed. Duplicate detection is a bag-of-words baseline until
hybrid search with pgvector (FR-ANA-5) replaces it.

This set contains 28 Spanish, one Portuguese and one English example. It is a pipeline smoke
test, not evidence of regional-language quality or production safety. A future benchmark needs
held-out/adversarial scenarios, more danger cases, per-language results and recorded model/hardware
versions. Synthetic cloud evaluation instructions: [`docs/evals.md`](docs/evals.md).

### For reviewers: what this prototype shows

- **A technical core.** A reporting pipeline where the safety properties are enforced in code, not
  asked of a model: field-level envelope encryption, a separate reporter origin with no
  third-party requests, a single-tool bounded model call whose every quote and term is checked in
  code, and a hash-chained audit trail of every staff read.
- **A proposed focus.** Neural data and mental integrity appear in the starter taxonomy alongside
  automated decisions and emotion recognition. Their usefulness and wording need participant review.
- **An integrated workflow.** Spanish-first intake, encrypted storage, grounded triage and human
  review in one prototype. This is a proposed adaptation, not a claim that other reporting tools
  do not exist or that this tool is uniquely safe.
- **Reproducible checks.** Unit/reporter suites, container-backed integration tests and the Mock
  eval can be run with the commands above. Record a fresh build and executed-test counts for the
  revision being demonstrated; old binaries are not evidence for new source.
- **Community participation: proposed approach.** Participants would help decide whether the tool
  is useful, what to collect, what not to publish and whether to pilot at all. See
  [`docs/community-participation.md`](docs/community-participation.md). Configurable forms and taxonomy
  support this process but do not establish completed co-design or community ownership.

Server-rendered forms, labels and local resources are implemented. Browser-network checks,
mobile/slow-device testing, axe and a manual screen-reader review are still needed before
claiming WCAG conformance or demonstrated 3G usability.

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

Phase 0 (prototype). Open questions for participants and a potential deploying organization are in
[`docs/open-questions.md`](docs/open-questions.md), the threat model in
[`docs/threat-model.md`](docs/threat-model.md), and the security policy in
[`SECURITY.md`](SECURITY.md). Contributions: [`CONTRIBUTING.md`](CONTRIBUTING.md) and
[`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md).

### License and credits

[MPL-2.0](LICENSE). Testigo is built on [Aktor](NOTICE), an actor-based multi-agent runtime by the
same author, relicensed here under MPL-2.0. See [`NOTICE`](NOTICE).
