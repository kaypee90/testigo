using System.Net;
using System.Text.Json.Nodes;
using AgentRuntime.LLM;
using AgentRuntime.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace AgentRuntime.Tests;

public sealed class ImageProviderContractTests
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN1sAAAAASUVORK5CYII=";

    [Theory]
    [InlineData("OpenAI", "gpt-4o", false)]
    [InlineData("OpenAI", "gpt-6-astra", true)]
    [InlineData("Anthropic", "claude-sonnet-5", false)]
    [InlineData("Gemini", "gemini-3-pro", false)]
    [InlineData("Ollama", "qwen3-vl:8b", false)]
    public async Task Images_are_native_parts_and_survive_a_tool_round_trip(string provider, string model, bool responses)
    {
        var handler = new CaptureHandler(provider, responses);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://provider.test/") };
        var options = Options.Create(new LlmOptions { Provider = provider, Model = model, ApiKey = "test" });
        ILLMProvider llm = provider switch
        {
            "Anthropic" => new AnthropicProvider(http, options),
            "Gemini" => new GeminiProvider(http, options),
            "Ollama" => new OllamaProvider(http, options),
            _ => new OpenAIProvider(http, options)
        };
        var messages = new List<ChatMessage>
        {
            ChatMessage.System("Inspect the images."),
            ChatMessage.User("Compare these.") with { Images = [new("image/png", Png), new("image/webp", "UklGRg==")] },
            new() { Role = ChatRole.Assistant, ToolCalls = [new() { Id = "read-1", Name = "filesystem_read", ArgumentsJson = "{}" }] },
            new() { Role = ChatRole.Tool, ToolCallId = "read-1", ToolName = "filesystem_read", Content = "{}" },
            // Image-only user messages are valid too.
            ChatMessage.User("") with { Images = [new("image/png", Png)] }
        };
        await llm.CompleteAsync(new LlmCompletionRequest { Messages = messages });
        var body = handler.Body!;
        var conversation = body[provider == "Gemini" ? "contents" : responses ? "input" : "messages"]!.AsArray();
        var first = conversation[provider is "OpenAI" or "Ollama" && !responses ? 1 : 0]!;
        var last = conversation[^1]!;
        switch (provider)
        {
            case "OpenAI":
                var parts = first["content"]!.AsArray();
                Assert.Equal(3, parts.Count);
                Assert.Equal("Compare these.", parts[0]!["text"]!.GetValue<string>());
                Assert.Equal(responses ? "input_image" : "image_url", parts[1]!["type"]!.GetValue<string>());
                var url = responses ? parts[1]!["image_url"] : parts[1]!["image_url"]!["url"];
                Assert.Equal("data:image/png;base64," + Png, url!.GetValue<string>());
                Assert.Single(last["content"]!.AsArray());
                Assert.Equal(responses ? "/v1/responses" : "/v1/chat/completions", handler.Path);
                break;
            case "Anthropic":
                Assert.Equal("image", first["content"]![0]!["type"]!.GetValue<string>());
                Assert.Equal("image/png", first["content"]![0]!["source"]!["media_type"]!.GetValue<string>());
                Assert.Equal(Png, first["content"]![0]!["source"]!["data"]!.GetValue<string>());
                Assert.Equal("image/webp", first["content"]![1]!["source"]!["media_type"]!.GetValue<string>());
                Assert.Single(last["content"]!.AsArray());
                Assert.Equal("ephemeral", last["content"]![0]!["cache_control"]!["type"]!.GetValue<string>());
                break;
            case "Gemini":
                Assert.Equal("Compare these.", first["parts"]![0]!["text"]!.GetValue<string>());
                Assert.Equal("image/png", first["parts"]![1]!["inlineData"]!["mimeType"]!.GetValue<string>());
                Assert.Equal(Png, first["parts"]![1]!["inlineData"]!["data"]!.GetValue<string>());
                Assert.Equal("image/webp", first["parts"]![2]!["inlineData"]!["mimeType"]!.GetValue<string>());
                Assert.Single(last["parts"]!.AsArray());
                break;
            case "Ollama":
                Assert.Equal("Compare these.", first["content"]!.GetValue<string>());
                Assert.Equal(Png, first["images"]![0]!.GetValue<string>());
                Assert.Equal("UklGRg==", first["images"]![1]!.GetValue<string>());
                Assert.Single(last["images"]!.AsArray());
                break;
        }
        Assert.DoesNotContain("base64", messages[1].Content!);
    }

    private sealed class CaptureHandler(string provider, bool responses) : HttpMessageHandler
    {
        public JsonNode? Body { get; private set; }
        public string? Path { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            var reply = provider switch
            {
                "Anthropic" => """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn"}""",
                "Gemini" => """{"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}]}""",
                "Ollama" => """{"message":{"content":"ok"},"done":true}""",
                _ when responses => """{"output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}""",
                _ => """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}"""
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply) };
        }
    }
}
