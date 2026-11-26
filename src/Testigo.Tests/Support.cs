using AgentRuntime.LLM;
using Testigo.Taxonomy;
using Testigo.Triage;

namespace Testigo.Tests;

/// <summary>A scripted LLM for triage tests: returns what the test gives it and counts calls.</summary>
internal sealed class ScriptedLlm(Func<LlmCompletionRequest, LlmCompletionResponse> respond) : ILLMProvider
{
    public List<LlmCompletionRequest> Requests { get; } = [];
    public string ProviderName => "scripted";

    public Task<LlmCompletionResponse> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static ScriptedLlm Arguments(string json) => new(_ => Call(json));

    public static LlmCompletionResponse Call(string json) => new()
    {
        ToolCalls = [new ToolCall { Id = "call_1", Name = TriagePrompt.ToolName, ArgumentsJson = json }],
        FinishReason = LlmFinishReason.ToolCalls,
        InputTokens = 100,
        OutputTokens = 50,
    };
}

internal sealed class ThrowingLlm : ILLMProvider
{
    public int Calls { get; private set; }
    public string ProviderName => "throwing";

    public Task<LlmCompletionResponse> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        throw new HttpRequestException("model unavailable");
    }
}

internal sealed class MemoryTriageCache : ITriageCache
{
    public Dictionary<string, string> Items { get; } = [];
    public Task<string?> GetAsync(string organizationId, string cacheKey, CancellationToken cancellationToken) => Task.FromResult(Items.GetValueOrDefault(organizationId + "|" + cacheKey));
    public Task PutAsync(string organizationId, string cacheKey, string argumentsJson, CancellationToken cancellationToken)
    {
        Items[organizationId + "|" + cacheKey] = argumentsJson;
        return Task.CompletedTask;
    }
}

internal static class Fixtures
{
    public static readonly HarmTaxonomy Taxonomy = HarmTaxonomy.LoadDefault();
    public static readonly DangerDetector Danger = new(DangerRuleSet.LoadDefault());

    public static TriageService Triage(ILLMProvider llm, ITriageCache? cache = null, TriageOptions? options = null) =>
        new(llm, Taxonomy, Danger, cache ?? new NoTriageCache(), options ?? new TriageOptions());
}
