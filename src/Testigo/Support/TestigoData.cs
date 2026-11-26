using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testigo.Support;

/// <summary>
/// Reads Testigo's data files: the embedded starting versions, or a file a deployment names in
/// configuration instead (taxonomy, form and support contacts are data, not code).
/// </summary>
public static class TestigoData
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Enum names are read case-insensitively: "longtext" is FieldKind.LongText.
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The embedded file with this name (in Testigo/Data), or the file at <paramref name="overridePath"/> when set.</summary>
    public static T Load<T>(string embeddedName, string? overridePath = null)
    {
        string text;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            text = File.ReadAllText(overridePath);
        }
        else
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Testigo.Data.{embeddedName}")
                ?? throw new InvalidOperationException($"Data file '{embeddedName}' isn't embedded.");
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }

        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new InvalidOperationException($"Data file '{embeddedName}' is empty.");
    }
}

/// <summary>Emergency and support contacts per country (FR-ADM-6, FR-INT-9, FR-ANA-6). VALIDATE.</summary>
public sealed record SupportContacts
{
    public string Version { get; init; } = string.Empty;
    public Dictionary<string, CountryContacts> Countries { get; init; } = [];

    public CountryContacts? For(string? country) =>
        country is not null && Countries.TryGetValue(country, out var c) ? c : Countries.GetValueOrDefault("CL");

    public static SupportContacts LoadDefault(string? overridePath = null) => TestigoData.Load<SupportContacts>("support-contacts-v1.json", overridePath);
}

public sealed record CountryContacts
{
    public List<SupportContact> Emergency { get; init; } = [];
    public List<SupportContact> Support { get; init; } = [];
    /// <summary>What reporters are told about response times (FR-INT-9, configurable).</summary>
    public string ResponseTime { get; init; } = string.Empty;
    /// <summary>The response time in other languages, by language code (FR-INT-8).</summary>
    public Dictionary<string, string> ResponseTimes { get; init; } = [];

    /// <summary>These contacts with their words in <paramref name="language"/> where there is a translation.</summary>
    public CountryContacts In(string? language) => language is null ? this : this with
    {
        Emergency = [.. Emergency.Select(c => c.In(language))],
        Support = [.. Support.Select(c => c.In(language))],
        ResponseTime = ResponseTimes.GetValueOrDefault(language) ?? ResponseTime,
        ResponseTimes = [],
    };
}

public sealed record SupportContact
{
    public required string Label { get; init; }
    public string? Phone { get; init; }
    public string? Url { get; init; }
    /// <summary>The label in other languages, by language code (FR-INT-8).</summary>
    public Dictionary<string, string> Labels { get; init; } = [];

    public SupportContact In(string language) => this with { Label = Labels.GetValueOrDefault(language) ?? Label, Labels = [] };
}
