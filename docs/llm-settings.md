# Choosing a model

Triage (requirements §5.3) uses one model provider for the whole server, set in configuration
(section `Llm`, or the `LLM_*` variables in `.env` with Docker Compose). Organizations don't pick
their own models.

| `Llm:Provider` | Needs | Notes |
|---|---|---|
| **Mock** (default) | nothing | Deterministic, scripted answers (`IMockLlmBehavior`). For the demo, the tests and CI. |
| **Ollama** | an address | A local model, so report text never leaves your servers (NFR-AI-1). The model must support tool calling; `qwen3:4b-instruct` is a good start. |
| **OpenAI** | API key | Also any OpenAI-compatible service (OpenRouter, Groq, vLLM, LM Studio): set `Llm:BaseUrl`. |
| **Anthropic** | API key | |
| **Gemini** | API key | Google AI Studio keys. |

With a hosted provider, **redacted** report text leaves the server and goes to that company
(NFR-AI-2). Read [data-protection.md](data-protection.md) before turning one on, and agree it
with the deploying organization: it is a VALIDATE item.

## Settings

| Setting | Env (compose) | Default | |
|---|---|---|---|
| `Llm:Provider` | `LLM_PROVIDER` | `Mock` | One of the names above. |
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

- Report text is redacted first; the model sees only the redacted copy, quoted as untrusted input.
- The model answers through one structured tool (`submit_triage`). It has no tool that publishes,
  sends, deletes or changes permissions (FR-ANA-8, NFR-SEC-12).
- Each report gets one attempt. If the call fails, times out or returns something invalid, the
  case is marked "analysis incomplete" for a person (NFR-AI-5).
- Every suggestion must quote the report; quotes that don't appear in it are dropped.

To add a provider, see [extending.md](extending.md#a-new-model-provider).
