using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testigo.Infrastructure;
using Testigo.Infrastructure.Triage;

namespace Testigo.Tests;

/// <summary>NFR-AI-1, NFR-AI-2: unsafe case-model configuration fails before any report is loaded.</summary>
public sealed class CaseModelPolicyTests
{
    [Theory]
    [InlineData("OpenAI", null, null)]
    [InlineData("Anthropic", null, null)]
    [InlineData("Gemini", null, null)]
    [InlineData("OpenAI", "https://api.openai.com", null)]
    [InlineData("unknown", null, null)]
    [InlineData("Ollama", "https://ollama.com", null)]
    [InlineData("Ollama", "http://8.8.8.8", null)]
    [InlineData("Ollama", "http://localhost@remote.example", null)]
    [InlineData("Ollama", "http://localhost:11434", "example:cloud")]
    public void Hosted_unknown_and_cloud_backed_providers_are_rejected(string provider, string? url, string? model)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:Provider"] = provider, ["Llm:BaseUrl"] = url, ["Llm:Model"] = model,
            ["Testigo:LocalModelsOnly"] = "true",
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTestigo(config));
        Assert.DoesNotContain("ConnectionStrings", error.Message); // rejected before database setup
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Mock", null)]
    [InlineData("Ollama", null)]
    [InlineData("Ollama", "http://localhost:11434")]
    [InlineData("Ollama", "http://ollama:11434")]
    [InlineData("Ollama", "http://host.docker.internal:11434")]
    [InlineData("Ollama", "http://192.168.1.4:11434")]
    [InlineData("Ollama", "http://[::1]:11434")]
    [InlineData("OpenAI", "http://localhost:1234/v1")]
    public void Local_configuration_is_accepted(string? provider, string? url) =>
        CaseModelPolicy.Validate(provider, url, "qwen3:4b-instruct", localOnly: true);

    /// <summary>Provider names match in any case, as AddLlmProvider and the eval CLI read them.</summary>
    [Theory]
    [InlineData("mock")]
    [InlineData("MOCK")]
    [InlineData("ollama")]
    [InlineData(" Ollama ")]
    [InlineData("openai")]
    public void Provider_names_are_accepted_in_any_case(string provider)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:Provider"] = provider, ["Llm:Model"] = "qwen3:4b-instruct",
        }).Build();
        // Past the model policy, AddTestigo stops only at the missing database setting.
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTestigo(config));
        Assert.Contains("ConnectionStrings", error.Message);
        CaseModelPolicy.Validate(provider, provider.Trim().ToLowerInvariant() == "openai" ? "http://localhost:1234/v1" : null, "qwen3:4b-instruct", localOnly: true);
    }

    [Theory]
    [InlineData(null, "Mock")]
    [InlineData("", "Mock")]
    [InlineData("ollama", "Ollama")]
    [InlineData("GEMINI", "Gemini")]
    [InlineData("acme", "acme")]
    public void Canonical_provider_names(string? configured, string expected) =>
        Assert.Equal(expected, AgentRuntime.LLM.LlmOptions.CanonicalProvider(configured));

    [Theory]
    [InlineData("OpenAI", "https://compatible.example/v1")]
    [InlineData("Anthropic", null)]
    [InlineData("Gemini", null)]
    [InlineData("Ollama", "https://models.example")]
    public void Supported_providers_remain_configurable_without_local_only_opt_in(string provider, string? url) =>
        CaseModelPolicy.Validate(provider, url, "example-model", localOnly: false);
}
