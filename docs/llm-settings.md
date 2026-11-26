# Choosing a model

Triage (requirements §5.2) uses one model provider for the whole server, set in configuration
(section `Llm`, or the `LLM_*` variables in `.env` with Docker Compose). Organizations don't pick
their own models.

| `Llm:Provider` | Needs | Notes |
|---|---|---|
| **Mock** (default) | nothing | Deterministic, scripted answers (`IMockLlmBehavior`). For the demo, the tests and CI. |
| **Ollama** | an address | Use an operator-controlled server and local weights. The model must support tool calling; `qwen3:4b-instruct` is a starting candidate, not an evaluated recommendation. |
| **OpenAI** | API key | Also any OpenAI-compatible service (OpenRouter, Groq, vLLM, LM Studio): set `Llm:BaseUrl`. |
| **Anthropic** | API key | |
| **Gemini** | API key | Google AI Studio keys. |

All supported providers remain configurable. Mock is the default. With a hosted provider, text
leaves the server **after rule-based redaction, which can miss identifiers**. Model-assisted
redaction happens afterward and does not undo that exposure. The reporter notice identifies
the provider, and for a hosted endpoint its hostname; a local server's address is never shown
on the public page. Phase 0 accepts invented scenarios only.

## Opt in to local models only

Set `TESTIGO_LOCAL_MODELS_ONLY=true` in Compose, or `Testigo:LocalModelsOnly=true` in .NET
configuration. The default is `false`; this switch does not change the default provider (Mock).
When enabled, startup rejects hosted endpoints and permits Mock or model servers at loopback,
private IPs, `ollama`, `host.docker.internal` or `host.containers.internal`. Local compatible APIs
also work: for example `Llm:Provider=OpenAI` with `Llm:BaseUrl=http://localhost:1234/v1`.
Explicitly cloud-backed model names are rejected and HTTP redirects are disabled. Operators must still ensure
the endpoint actually runs local weights and does not forward requests elsewhere.

With the switch off, OpenAI-compatible endpoints and other supported providers work as before.
Changing this switch is not permission to submit real data: a real-data cloud deployment would
need a reviewed outbound-data boundary, consent and legal review that the prototype does not supply.

## Settings

| Setting | Env (compose) | Default | |
|---|---|---|---|
| `Llm:Provider` | `LLM_PROVIDER` | `Mock` | One of the names above. |
| `Testigo:LocalModelsOnly` | `TESTIGO_LOCAL_MODELS_ONLY` | `false` | Opt-in local-only startup enforcement. |
| `Llm:Model` | `LLM_MODEL` | `mock` | The provider's model id. |
| `Llm:ApiKey` | `LLM_API_KEY` | | Hosted providers only. Keep it out of the repository: `.env` is ignored by git. |
| `Llm:BaseUrl` | `LLM_BASE_URL` | the provider's own | For Ollama in Docker, blank means the Docker host (`http://host.docker.internal:11434`); use `http://ollama:11434` with the bundled `ollama` profile. For OpenAI-compatible services, a trailing `/v1` or `/v1/chat/completions` is dropped, because the provider adds the path itself. |
| `Llm:TimeoutSeconds` | `LLM_TIMEOUT_SECONDS` | `300` | One model call. Local models on a slow machine can take minutes. |
| `Llm:ContextLength` | `LLM_CONTEXT_LENGTH` | `16384` | Ollama's context window. |
| `Llm:DisableThinking` | `LLM_DISABLE_THINKING` | `true` | Asks Ollama not to produce a reasoning trace. |
| `Llm:ReasoningMinOutputTokens` | | `25000` | Floor on the output cap for OpenAI reasoning models, whose reasoning comes out of the same allowance. |

## Local model with Ollama

```bash
# .env
LLM_PROVIDER=Ollama
LLM_MODEL=qwen3:4b-instruct
LLM_BASE_URL=http://ollama:11434

docker compose --profile ollama up -d --build
docker compose exec ollama ollama pull qwen3:4b-instruct
```

The bundled Ollama container isn't published outside the Docker network: it has no
authentication. Run the eval against the model before using it
([evals.md](evals.md#running-it)).

## How a model is used

- Rules replace detected identifiers first; the model sees that intermediate copy as untrusted
  input. Identifiers missed by rules can still be present. A second pass updates the final copy.
- The model answers through one structured tool (`submit_triage`). It has no tool that publishes,
  sends, deletes or changes permissions (FR-ANA-8, NFR-SEC-12).
- Each report gets one attempt. If the call fails, times out or returns something invalid, the
  case is marked "analysis incomplete" for a person (NFR-AI-5).
- Every suggestion must quote the report; quotes that don't appear in it are dropped.

To add a provider, see [extending.md](extending.md#a-new-model-provider).
