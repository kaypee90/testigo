using System.Net;
using AgentRuntime.LLM;

namespace Testigo.Infrastructure.Triage;

/// <summary>
/// NFR-AI-1, NFR-AI-2: optional local-model-only policy. Operators remain responsible for the server behind a
/// local address and for ensuring that it runs local weights, not a cloud-backed model.
/// </summary>
public static class CaseModelPolicy
{
    public static void Validate(string? provider, string? baseUrl, string? model = null, bool localOnly = false)
    {
        provider = LlmOptions.CanonicalProvider(provider);
        if (provider == "Mock") return;
        if (provider is not ("Ollama" or "OpenAI" or "Anthropic" or "Gemini"))
            throw new InvalidOperationException("Unknown model provider. Configure Mock, Ollama, OpenAI, Anthropic or Gemini.");
        if (!localOnly) return;
        if (provider != "Ollama" && string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Testigo:LocalModelsOnly requires a local endpoint instead of a hosted provider's default.");
        if (model?.Contains("cloud", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("Cloud-backed model names are not permitted with Testigo:LocalModelsOnly.");
        if (string.IsNullOrWhiteSpace(baseUrl)) return; // provider's loopback/container-host default
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0
            || uri.Query.Length > 0 || uri.Fragment.Length > 0 || !IsLocalHost(uri.Host))
            throw new InvalidOperationException("Local-model-only mode requires loopback, a private IP, or the bundled ollama/container-host name.");
    }

    private static bool IsLocalHost(string host)
    {
        if (host is "localhost" or "ollama" or "host.docker.internal" or "host.containers.internal") return true;
        if (!IPAddress.TryParse(host.Trim('[', ']'), out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168
            : (bytes[0] & 0xfe) == 0xfc;
    }
}
