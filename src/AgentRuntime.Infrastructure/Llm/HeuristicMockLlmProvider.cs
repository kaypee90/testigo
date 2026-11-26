using AgentRuntime.LLM;

namespace AgentRuntime.Infrastructure.Llm;

/// <summary>
/// A deterministic stand-in for one kind of request the Mock provider can answer, recognised by
/// the tool offered (Testigo's triage registers one). Registered in DI; checked in order. This is
/// the extension point for scripting the Mock provider for a new feature.
/// </summary>
public interface IMockLlmBehavior
{
    bool Handles(LlmCompletionRequest request);
    LlmCompletionResponse Respond(LlmCompletionRequest request);
}

/// <summary>
/// The "Mock" provider: no model, no key, no network. It answers with the registered
/// <see cref="IMockLlmBehavior"/> that handles the request, so the demo and the tests run the
/// same way every time. A request no behavior handles gets a plain text answer.
/// </summary>
public sealed class HeuristicMockLlmProvider(IEnumerable<IMockLlmBehavior>? behaviors = null) : ILLMProvider
{
    private readonly IReadOnlyList<IMockLlmBehavior> behaviors = behaviors?.ToList() ?? [];

    public string ProviderName => "Mock";

    public Task<LlmCompletionResponse> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken = default)
    {
        foreach (var behavior in behaviors)
        {
            if (behavior.Handles(request)) return Task.FromResult(behavior.Respond(request));
        }

        var input = string.Concat(request.Messages.Select(m => m.Content));
        return Task.FromResult(new LlmCompletionResponse
        {
            Content = "Mock answer: no scripted behavior handles this request.",
            FinishReason = LlmFinishReason.Stop,
            InputTokens = Math.Max(1, input.Length / 4),
            OutputTokens = 10
        });
    }
}
