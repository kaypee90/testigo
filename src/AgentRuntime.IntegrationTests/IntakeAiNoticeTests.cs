using AgentRuntime.Api.Controllers;
using Xunit;

namespace AgentRuntime.IntegrationTests;

/// <summary>
/// FR-INT-2, NFR-AI-1, NFR-SEC-2: the public reporter notice says where the model runs, but never
/// shows a local server's address. Pure functions: no database needed.
/// </summary>
public sealed class IntakeAiNoticeTests
{
    [Theory]
    [InlineData(null, null, "mock")]
    [InlineData("mock", null, "mock")]
    [InlineData("MOCK", null, "mock")]
    [InlineData("ollama", null, "local")]
    [InlineData("Ollama", "http://192.168.1.4:11434", "local")]
    [InlineData("openai", "http://localhost:1234/v1", "local")]
    [InlineData("OpenAI", null, "cloud")]
    [InlineData("Ollama", "https://models.example", "cloud")]
    public void The_mode_follows_the_provider_in_any_case(string? provider, string? baseUrl, string expected) =>
        Assert.Equal(expected, TestigoIntakeController.AiMode(provider, baseUrl, "qwen3:4b-instruct"));

    [Theory]
    [InlineData("local", "http://192.168.1.4:11434")]
    [InlineData("local", "http://ollama:11434")]
    [InlineData("local", "http://host.docker.internal:11434")]
    [InlineData("mock", "http://10.0.4.17:11434")]
    [InlineData("cloud", null)]
    public void Local_and_mock_endpoints_are_never_shown(string mode, string? baseUrl) =>
        Assert.Null(TestigoIntakeController.PublicEndpoint(mode, baseUrl));

    [Fact]
    public void A_hosted_endpoint_shows_its_host_only() =>
        Assert.Equal("models.example", TestigoIntakeController.PublicEndpoint("cloud", "https://user:secret@models.example/v1?key=x"));
}
