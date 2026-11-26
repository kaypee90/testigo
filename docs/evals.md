# Evals

`testigo-eval` (`src/Testigo.Evals`) scores the triage pipeline (requirements §10.2) on the 30
synthetic reports in `evals/testigo/reports.jsonl`. It runs the real `TriageService` with the Mock
provider (as CI does) or a local Ollama model, and computes every number in code from the
labels:

- citation validity and evidence offsets;
- redaction leakage (personal data left in the anonymized copy);
- immediate-danger recall and precision;
- determinism (the same report gives the same result);
- duplicate and same-actor detection;
- classification per taxonomy dimension;
- prompt injection (reports that try to give the model instructions).

It exits with code 1 when a §10.2 target is missed, and refuses hosted providers, so report text
never leaves the machine during an eval.

## Running it

```bash
dotnet run --project src/Testigo.Evals                                   # Mock provider, as in CI
dotnet run --project src/Testigo.Evals -- --provider Ollama --model qwen3:4b-instruct
dotnet run --project src/Testigo.Evals -- --help
```

| Option | |
|---|---|
| `--provider Mock\|Ollama` | Default `Mock`. |
| `--model <name>` | The Ollama model. |
| `--base-url <url>` | Ollama's address (default `http://localhost:11434`). |
| `--reports <file>` | Another eval set in the same format. |
| `--verbose` | Per report: the labelled terms the model missed, the terms it added, and the related-report links it suggested. |

The Mock provider's answers are scripted (`MockTriageBehavior`), so its numbers show that the
pipeline (redaction, grounding, danger rules, scoring) works, not how good a model is. Run it with
a real local model before choosing one.

## The eval set

Every report is synthetic: invented people, companies and events, and Chilean RUTs that fail the
check digit (requirements §10.4). `evals/testigo/make_reports.py` generates the set; change the
generator, not the `.jsonl`, and keep the labels in sync with the taxonomy's keys. Add reports for
each new taxonomy term, danger rule or analysis ([extending.md](extending.md)).
