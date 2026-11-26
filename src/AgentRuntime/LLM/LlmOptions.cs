namespace AgentRuntime.LLM;

/// <summary>
/// The model the server uses (section "Llm"). One provider for the whole server: Mock (scripted,
/// for demos and tests), Ollama (local, the default for real use, NFR-AI-1), or a hosted provider
/// (OpenAI, Anthropic, Gemini), which only ever receives redacted text (NFR-AI-2).
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; set; } = "Mock";
    public string Model { get; set; } = "default";
    public string? ApiKey { get; set; }

    /// <summary>The provider's address; empty uses the provider's public one (or Ollama on this machine).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>HTTP timeout for one model call. Local models can take minutes on a slow machine.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>Floor on the output cap for reasoning models (OpenAI o-series, GPT-5 and later), whose
    /// reasoning tokens come out of the same allowance: a smaller cap can be spent before any answer
    /// is written. A cap is a ceiling, not a cost. 0 disables the floor.</summary>
    public int ReasoningMinOutputTokens { get; set; } = 25_000;

    /// <summary>Context window requested from providers that let the caller choose it (Ollama).</summary>
    public int ContextLength { get; set; } = 16384;

    /// <summary>Ask providers that support it (Ollama) not to generate a reasoning trace.</summary>
    public bool DisableThinking { get; set; } = true;

    /// <summary>The providers this server knows, in their canonical spelling.</summary>
    public static IReadOnlyList<string> KnownProviders { get; } = ["Mock", "Ollama", "OpenAI", "Anthropic", "Gemini"];

    /// <summary>
    /// The canonical name for a configured provider: blank means Mock, and a known name matches in
    /// any case ("ollama" is Ollama). An unknown name comes back trimmed but otherwise unchanged, so
    /// callers can reject it instead of silently using another provider.
    /// </summary>
    public static string CanonicalProvider(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return "Mock";
        var name = configured.Trim();
        return KnownProviders.FirstOrDefault(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
    }

    /// <summary>
    /// A base URL as the providers use it. OpenAI-compatible services document theirs with the API
    /// version on the end, but the OpenAI provider adds <c>v1/chat/completions</c> itself, so a
    /// trailing <c>/v1</c> (or <c>/v1/chat/completions</c>) is dropped rather than sent twice.
    /// </summary>
    public static string? NormalizeBaseUrl(string? provider, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim().TrimEnd('/');
        if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var suffix in (string[])["/chat/completions", "/responses", "/v1"])
            {
                if (u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) u = u[..^suffix.Length];
            }
        }

        return u;
    }
}
