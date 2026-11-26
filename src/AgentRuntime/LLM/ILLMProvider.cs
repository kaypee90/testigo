namespace AgentRuntime.LLM;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record ToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ArgumentsJson { get; init; }

    /// <summary>Opaque data the provider requires back with this call on the next request
    /// (Gemini's thought signature). Stored with the transcript; other providers ignore it.</summary>
    public string? ProviderSignature { get; init; }
}

public sealed record ChatMessage
{
    public required ChatRole Role { get; init; }
    public string? Content { get; init; }

    /// <summary>Transient image inputs. Never persisted in the agent transcript or event stream.</summary>
    public IReadOnlyList<ChatImage> Images { get; init; } = [];

    /// <summary>Populated on an Assistant message when the model chose to call tools.</summary>
    public List<ToolCall>? ToolCalls { get; init; }

    /// <summary>Populated on a Tool message: which call this is the result of.</summary>
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }

    /// <summary>System messages: the first this-many characters are the same on every call
    /// (role, rules, tool guidance) and the rest changes (status, usage, time). Providers with
    /// explicit prompt caching (Anthropic) mark the boundary so the stable part is served from cache.</summary>
    public int? CacheablePrefixLength { get; init; }

    public static ChatMessage System(string content) => new() { Role = ChatRole.System, Content = content };
    public static ChatMessage User(string content) => new() { Role = ChatRole.User, Content = content };
}

/// <summary>An image sent as native visual input, not as base64 text in the prompt.</summary>
public sealed record ChatImage(string MediaType, string Base64Data);

public sealed record LlmToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string JsonSchema { get; init; }
}

public sealed record LlmCompletionRequest
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public IReadOnlyList<LlmToolDefinition> Tools { get; init; } = [];
    public string? Model { get; init; }
    public int MaxTokens { get; init; } = 4096;
    public double Temperature { get; init; } = 0.4;
}

public enum LlmFinishReason
{
    Stop,
    ToolCalls,
    MaxTokens,
    Error
}

public sealed record LlmCompletionResponse
{
    public string? Content { get; init; }
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];
    public LlmFinishReason FinishReason { get; init; }
    /// <summary>All input tokens, including those read from or written to the prompt cache.</summary>
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    /// <summary>Input tokens served from the provider's prompt cache (billed at a discount).</summary>
    public int CachedInputTokens { get; init; }
    /// <summary>Input tokens written to the cache on this call (Anthropic bills these at a premium).</summary>
    public int CacheWriteInputTokens { get; init; }
}

/// <summary>
/// Provider-agnostic chat with structured tool calls. Testigo's triage depends on this, never on a
/// provider's own types.
/// </summary>
public interface ILLMProvider
{
    string ProviderName { get; }

    Task<LlmCompletionResponse> CompleteAsync(
        LlmCompletionRequest request, CancellationToken cancellationToken = default);
}
