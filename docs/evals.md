# Evals

`testigo-eval` (`src/Testigo.Evals`) scores the triage pipeline (requirements §10.2) on the 30
synthetic reports in `evals/testigo/reports.jsonl`. It runs the real `TriageService` with the Mock
provider (as CI does), local Ollama or an explicitly enabled hosted provider, and computes
every number in code from the labels:

- citation validity and evidence offsets;
- known identifiers remaining in the **final analysis copy**;
- known identifiers in **actual model requests**, before model-assisted redaction;
- immediate-danger recall and precision;
- determinism (the same report gives the same result);
- duplicate and same-actor detection;
- classification per taxonomy dimension;
- prompt injection (reports that try to give the model instructions).

It exits with code 1 when a §10.2 target is missed. Request exposure is reported separately,
not hidden behind final-copy success. The current Mock run leaves 0/11 known identifiers in final
copies but sends 1/11 to Mock across 63 requests. Mock makes no external call; the same ordering
with a hosted provider would expose that synthetic identifier. Neither number proves anonymity.

## Running it

```bash
dotnet run --project src/Testigo.Evals                                   # Mock provider, as in CI
dotnet run --project src/Testigo.Evals -- --provider Ollama --model qwen3:4b-instruct
dotnet run --project src/Testigo.Evals -- --help
```

| Option | |
|---|---|
| `--provider Mock\|Ollama\|OpenAI\|Anthropic\|Gemini` | Default `Mock`. |
| `--model <name>` | Model ID; required for hosted providers. |
| `--base-url <url>` | Local Ollama address or a hosted provider/compatible endpoint. |
| `--reports <file>` | Another synthetic set, for Mock/local Ollama only. |
| `--allow-cloud-synthetic` | Explicitly permit sending the bundled fictional set to a hosted provider. |
| `--verbose` | Per report: the labelled terms the model missed, the terms it added, and the related-report links it suggested. |

The Mock provider's answers are scripted (`MockTriageBehavior`), so its numbers show that the
pipeline (redaction, grounding, danger rules, scoring) works, not how good a model is. Run it with
a real local model before choosing one.

### Testing a cloud model

Set `TESTIGO_EVAL_API_KEY` in your shell or secret manager, never in a committed file or CLI
argument. Then replace `YOUR_MODEL_ID` with a model available to your provider:

```bash
dotnet run --project src/Testigo.Evals -- --provider OpenAI --model YOUR_MODEL_ID --allow-cloud-synthetic
```

Anthropic and Gemini use the same flag and environment variable. The CLI has no case-database
connection and refuses custom `--reports` paths for hosted runs. Inspect the bundled fictional
set before sending it; the flag is an acknowledgment, not a detector of real personal data.
Narratives and missed synthetic identifiers leave your machine, provider terms apply, and
calls may incur charges. A run uses up to two calls per report plus injection baselines, with
per-call time/output limits and no retry loop.

The application separately allows provider configuration, with opt-in local-only enforcement:
[`llm-settings.md`](llm-settings.md). Neither path is approved for real reports in Phase 0.

## The eval set

Every report is synthetic: invented people, companies and events, and Chilean RUTs that fail the
check digit (requirements §10.4). `evals/testigo/make_reports.py` generates the set; change the
generator, not the `.jsonl`, and keep the labels in sync with the taxonomy's keys. Add reports for
each new taxonomy term, danger rule or analysis ([extending.md](extending.md)).

Coverage is 28 Spanish, one Portuguese and one English example, with three danger cases.
These are smoke checks. Later evaluation needs reviewed annotations, held-out/adversarial
examples, per-language metrics and model/version/hardware records before any broader accuracy claim.
