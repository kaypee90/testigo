using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Testigo.Reporter;

public sealed record FormOptionDto(string Key, string Label);
public sealed record FormFieldDto(string Key, string Kind, bool Required, int MaxLength, string Label, string? Help, List<FormOptionDto> Options, string? Default, Dictionary<string, string>? OnlyWhen);
public sealed record ConsentDto(string Key, bool Required, string Label);
public sealed record ContactDto(string Label, string? Phone, string? Url);
public sealed record CountryContactsDto(List<ContactDto> Emergency, List<ContactDto> Support, string ResponseTime);

public sealed record FormDto(string Version, string NoticeVersion, string Language, List<string>? Languages, List<FormFieldDto> Fields, List<ConsentDto> Consents, List<FormOptionDto> ContactMethods,
    int PassphraseMinLength, Dictionary<string, CountryContactsDto> Contacts, string AiMode, string? AiProvider = null, string? AiEndpoint = null)
{
    public CountryContactsDto ContactsFor(string? country) =>
        country is not null && Contacts.TryGetValue(country, out var c) ? c : Contacts.GetValueOrDefault("CL") ?? new CountryContactsDto([], [], string.Empty);
}

public sealed record SubmissionDto
{
    public required string FormVersion { get; init; }
    public required Dictionary<string, List<string>> Answers { get; init; }
    public required List<string> Consents { get; init; }
    public string? ContactMethod { get; init; }
    public string? ContactValue { get; init; }
    public string? Passphrase { get; init; }
    /// <summary>The language the form was shown in: the notice version and error messages follow it.</summary>
    public string? Language { get; init; }
}

public sealed record ReceiptDto(string CaseCode, string? GeneratedPassphrase, bool ImmediateDanger, CountryContactsDto Contacts, string ResponseTime);
public sealed record FieldErrorDto(string Field, string Message);

public enum SubmitOutcome { Received, Invalid, Unavailable }

public sealed record SubmitResult(SubmitOutcome Outcome, ReceiptDto? Receipt = null, IReadOnlyList<FieldErrorDto>? Errors = null);

/// <summary>
/// Talks to the API's intake endpoints with the shared key. It sends only what the reporter
/// typed: no client address, no forwarded headers, no user agent of theirs (NFR-SEC-3).
/// </summary>
public sealed class IntakeClient(HttpClient http, IMemoryCache cache)
{
    public const string KeyHeader = "X-Testigo-Intake-Key";
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The form with its words in <paramref name="language"/> (the API falls back to Spanish).</summary>
    public async Task<FormDto?> FormAsync(string language, CancellationToken cancellationToken)
    {
        var cacheKey = FormCacheKey(language);
        if (cache.TryGetValue(cacheKey, out FormDto? cached)) return cached;
        try
        {
            var form = await http.GetFromJsonAsync<FormDto>($"api/intake/form?lang={Uri.EscapeDataString(language)}", Json, cancellationToken);
            if (form is not null) cache.Set(cacheKey, form, TimeSpan.FromMinutes(5));
            return form;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public async Task<SubmitResult> SubmitAsync(SubmissionDto submission, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/intake/reports", submission, Json, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var receipt = await response.Content.ReadFromJsonAsync<ReceiptDto>(Json, cancellationToken);
                return receipt is null ? new SubmitResult(SubmitOutcome.Unavailable) : new SubmitResult(SubmitOutcome.Received, receipt);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var body = await response.Content.ReadFromJsonAsync<ErrorsBody>(Json, cancellationToken);
                if (body?.Errors is { Count: > 0 } errors)
                {
                    // The form may have changed since it was cached.
                    if (errors.Any(e => e.Field == "form")) cache.Remove(FormCacheKey(submission.Language ?? TextCatalog.Default));
                    return new SubmitResult(SubmitOutcome.Invalid, Errors: errors);
                }
            }

            return new SubmitResult(SubmitOutcome.Unavailable);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new SubmitResult(SubmitOutcome.Unavailable);
        }
    }

    private static string FormCacheKey(string language) => "form:" + language;

    private sealed record ErrorsBody(List<FieldErrorDto>? Errors);

    /// <summary>The shared key from configuration or its file (re-read until it appears: the API writes it on first start).</summary>
    public static string? ReadKey(ReporterOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.IntakeKey)) return o.IntakeKey.Trim();
        if (string.IsNullOrWhiteSpace(o.IntakeKeyFile) || !File.Exists(o.IntakeKeyFile)) return null;
        var key = File.ReadAllText(o.IntakeKeyFile).Trim();
        return key.Length > 0 ? key : null;
    }
}

/// <summary>Adds the shared key to each intake request, reading the key file lazily.</summary>
public sealed class IntakeKeyHandler(IOptions<ReporterOptions> options) : DelegatingHandler
{
    private string? key;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        key ??= IntakeClient.ReadKey(options.Value);
        if (key is not null) request.Headers.Add(IntakeClient.KeyHeader, key);
        return base.SendAsync(request, cancellationToken);
    }
}
