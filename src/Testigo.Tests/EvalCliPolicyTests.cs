using Testigo.Evals;

namespace Testigo.Tests;

/// <summary>NFR-AI-2, §10.4: cloud evaluation requires intent and cannot consume a custom case export.</summary>
public sealed class EvalCliPolicyTests
{
    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    [InlineData("Gemini")]
    public async Task Hosted_runs_without_synthetic_acknowledgment_are_rejected_before_network_access(string provider) =>
        Assert.Equal(2, await TestigoEvalCli.RunAsync(["--provider", provider, "--model", "synthetic-test-model"]));

    /// <summary>A rejected local endpoint is a usage error (exit code 2), not an unhandled exception.</summary>
    [Theory]
    [InlineData("http://gpu-box.lan:11434")]
    [InlineData("http://8.8.8.8:11434")]
    public async Task A_non_local_ollama_endpoint_is_refused_with_a_usage_error(string baseUrl) =>
        Assert.Equal(2, await TestigoEvalCli.RunAsync(["--provider", "ollama", "--base-url", baseUrl]));

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    [InlineData("Gemini")]
    public async Task Acknowledgment_does_not_allow_custom_report_exports(string provider) =>
        Assert.Equal(2, await TestigoEvalCli.RunAsync([
            "--provider", provider, "--model", "synthetic-test-model", "--allow-cloud-synthetic",
            "--reports", "not-a-real-export.jsonl",
        ]));
}
