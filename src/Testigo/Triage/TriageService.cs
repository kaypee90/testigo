using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentRuntime.LLM;
using Testigo.Redaction;
using Testigo.Taxonomy;

namespace Testigo.Triage;

/// <summary>A suggested taxonomy term, grounded in the report (FR-ANA-4).</summary>
public sealed record SuggestedTerm(string Dimension, string Term, Evidence Evidence);

/// <summary>A suggested extracted field (actor, product, place, date), grounded in the report.</summary>
public sealed record SuggestedField(string Field, string Value, Evidence Evidence);

/// <summary>Where a suggestion's quote is in the original report, and the quote as written there.</summary>
public sealed record Evidence(int Start, int Length, string Quote);

/// <summary>The immediate-danger assessment (FR-ANA-6): rules and model, either raises the flag.</summary>
public sealed record DangerAssessment
{
    public bool Flag => ByRules || ByModel;
    public bool ByRules { get; init; }
    public bool ByModel { get; init; }
    public IReadOnlyList<DangerMatch> RuleMatches { get; init; } = [];
    public Evidence? ModelEvidence { get; init; }
    public string RulesVersion { get; init; } = string.Empty;
}

/// <summary>What the first pass produced for one report. Suggestions only (NFR-AI-3).</summary>
public sealed record TriageOutcome
{
    public required string Language { get; init; }
    /// <summary>The final analysis copy: rules plus the model-assisted step.</summary>
    public required RedactionResult Redaction { get; init; }
    public required DangerAssessment Danger { get; init; }
    public IReadOnlyList<SuggestedTerm> Terms { get; init; } = [];
    public IReadOnlyList<SuggestedField> Fields { get; init; } = [];
    /// <summary>Complete, or Incomplete when the budget ran out or the model failed (NFR-AI-5).</summary>
    public required bool Complete { get; init; }
    public string? IncompleteReason { get; init; }
    /// <summary>Quotes the model returned, and how many of them were found in the report (NFR-AI-4).</summary>
    public int QuotesReturned { get; init; }
    public int QuotesGrounded { get; init; }
    /// <summary>Suggestions dropped: no quote found, or a term outside the taxonomy.</summary>
    public int Dropped { get; init; }
    public required string Model { get; init; }
    public required string PromptVersion { get; init; }
    public required string TaxonomyVersion { get; init; }
    /// <summary>The cache key (FR-ANA-7): same input, same taxonomy, prompt and model give the same result.</summary>
    public required string CacheKey { get; init; }
    public bool FromCache { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    /// <summary>The tool arguments the model returned, kept so the result can be reproduced from the cache.</summary>
    public string? RawArguments { get; init; }
}

/// <summary>The analysis budget, enforced by the runtime (NFR-AI-5). Section <c>Testigo:Triage</c>.</summary>
public sealed record TriageOptions
{
    public const string Section = "Testigo:Triage";

    public int MaxOutputTokens { get; init; } = 2048;
    public int TimeoutSeconds { get; init; } = 180;
    /// <summary>Longer analysis copies aren't sent to a model; the case goes to a person.</summary>
    public int MaxInputCharacters { get; init; } = 16000;
    /// <summary>Recorded with the result; the router picks the actual model.</summary>
    public string ModelLabel { get; init; } = "server-default";
}

/// <summary>
/// Remembers model answers by cache key, so the same report text gives the same suggestions
/// (FR-ANA-7). Kept per organization: one organization's cached answers are never served to another.
/// </summary>
public interface ITriageCache
{
    Task<string?> GetAsync(string organizationId, string cacheKey, CancellationToken cancellationToken);
    Task PutAsync(string organizationId, string cacheKey, string argumentsJson, CancellationToken cancellationToken);
}

public sealed class NoTriageCache : ITriageCache
{
    public Task<string?> GetAsync(string organizationId, string cacheKey, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    public Task PutAsync(string organizationId, string cacheKey, string argumentsJson, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The automatic first pass on a report (FR-ANA-1): detect the language, redact by rules, run the
/// danger rules, then one bounded model call that returns classifications, fields, a danger check
/// and personal data the rules missed. Code then checks every quote against the text the model
/// saw, drops what it can't ground or what isn't in the taxonomy, and builds the final redacted
/// copy itself. One call, no retries: on failure the result is marked incomplete for a person.
/// </summary>
public sealed class TriageService(ILLMProvider llm, HarmTaxonomy taxonomy, DangerDetector danger, ITriageCache cache, TriageOptions options)
{
    private readonly Redactor redactor = new();

    public async Task<TriageOutcome> AnalyzeAsync(string organizationId, string original, CancellationToken cancellationToken)
    {
        var language = LanguageDetector.Detect(original);
        var rules = redactor.Redact(original);
        var ruleDanger = danger.Find(original);
        var cacheKey = CacheKey(rules.Text);
        var baseOutcome = new TriageOutcome
        {
            Language = language,
            Redaction = rules,
            Danger = new DangerAssessment { ByRules = ruleDanger.Count > 0, RuleMatches = ruleDanger, RulesVersion = danger.Version },
            Complete = false,
            Model = options.ModelLabel,
            PromptVersion = TriagePrompt.Version,
            TaxonomyVersion = taxonomy.Version,
            CacheKey = cacheKey,
        };

        if (rules.Text.Length > options.MaxInputCharacters)
        {
            return baseOutcome with { IncompleteReason = "El reporte es más largo de lo que el análisis automático puede leer. Requiere revisión manual." };
        }

        var cached = await cache.GetAsync(organizationId, cacheKey, cancellationToken);
        string arguments;
        int inputTokens = 0, outputTokens = 0;
        if (cached is not null)
        {
            arguments = cached;
        }
        else
        {
            var request = new LlmCompletionRequest
            {
                Messages = [ChatMessage.System(TriagePrompt.System()), ChatMessage.User(TriagePrompt.User(taxonomy, rules.Text))],
                Tools = [TriagePrompt.Tool(taxonomy)],
                MaxTokens = options.MaxOutputTokens,
                Temperature = 0,
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            LlmCompletionResponse response;
            try
            {
                response = await llm.CompleteAsync(request, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return baseOutcome with { IncompleteReason = "El análisis automático superó su tiempo máximo." };
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return baseOutcome with { IncompleteReason = "El modelo no respondió. Requiere revisión manual." };
            }

            inputTokens = response.InputTokens;
            outputTokens = response.OutputTokens;
            var call = response.ToolCalls.FirstOrDefault(c => c.Name == TriagePrompt.ToolName);
            if (call is null)
            {
                return baseOutcome with { IncompleteReason = "El modelo no entregó un triaje con el formato esperado.", InputTokens = inputTokens, OutputTokens = outputTokens };
            }

            arguments = ToolArguments.Repair(call.ArgumentsJson, request.Tools[0].JsonSchema);
        }

        var parsed = Parse(arguments, rules, original);
        if (parsed is null)
        {
            return baseOutcome with { IncompleteReason = "La respuesta del modelo no se pudo leer.", InputTokens = inputTokens, OutputTokens = outputTokens };
        }

        if (cached is null) await cache.PutAsync(organizationId, cacheKey, arguments, cancellationToken);
        var final = parsed.Extra.Count > 0 ? redactor.Redact(original, parsed.Extra) : rules;
        return baseOutcome with
        {
            Redaction = final,
            Danger = baseOutcome.Danger with { ByModel = parsed.DangerFlag, ModelEvidence = parsed.DangerEvidence },
            Terms = parsed.Terms,
            Fields = parsed.Fields,
            Complete = true,
            QuotesReturned = parsed.QuotesReturned,
            QuotesGrounded = parsed.QuotesGrounded,
            Dropped = parsed.Dropped,
            FromCache = cached is not null,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            RawArguments = arguments,
        };
    }

    public string CacheKey(string redactedText)
    {
        var input = $"{TriagePrompt.Version}\n{taxonomy.Version}\n{options.ModelLabel}\n{redactedText}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private sealed record Parsed(List<SuggestedTerm> Terms, List<SuggestedField> Fields, bool DangerFlag, Evidence? DangerEvidence, List<ExtraPersonalData> Extra, int QuotesReturned, int QuotesGrounded, int Dropped);

    /// <summary>Reads the tool arguments. Null when they aren't a JSON object at all.</summary>
    private Parsed? Parse(string json, RedactionResult seen, string original)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object) return null;
        int returned = 0, grounded = 0, dropped = 0;

        Evidence? Ground(JsonElement item)
        {
            var quote = Str(item, "quote");
            if (quote is null) return null;
            returned++;
            var at = QuoteLocator.Find(seen.Text, TriagePrompt.AsInReport(quote));
            if (at is null) return null;
            grounded++;
            var (start, length) = seen.ToOriginal(at.Start, at.Length);
            return new Evidence(start, length, original.Substring(start, length));
        }

        var terms = new List<SuggestedTerm>();
        foreach (var item in Array(root, "classifications"))
        {
            var dimension = Str(item, "dimension");
            var term = Str(item, "term");
            var evidence = Ground(item);
            if (dimension is null || term is null || evidence is null || !taxonomy.Has(dimension, term))
            {
                dropped++;
                continue;
            }

            if (terms.Any(t => t.Dimension == dimension && t.Term == term)) continue;
            if (taxonomy.Dimension(dimension) is { Multiple: false } && terms.Any(t => t.Dimension == dimension))
            {
                dropped++;
                continue;
            }

            terms.Add(new SuggestedTerm(dimension, term, evidence));
        }

        var fields = new List<SuggestedField>();
        foreach (var item in Array(root, "fields"))
        {
            var field = Str(item, "field");
            var value = Str(item, "value");
            var evidence = Ground(item);
            // A value must be in its own quote: nothing extracted is invented.
            if (field is null || value is null || evidence is null || !TriagePrompt.FieldKeys.Contains(field) || value.Length > 200
                || !FoldedText.Of(seen.Restore(evidence.Quote)).Folded.Contains(FoldedText.Of(seen.Restore(value)).Folded, StringComparison.Ordinal))
            {
                dropped++;
                continue;
            }

            if (fields.Any(f => f.Field == field)) continue;
            fields.Add(new SuggestedField(field, seen.Restore(value), evidence));
        }

        var dangerFlag = false;
        Evidence? dangerEvidence = null;
        if (root.TryGetProperty("immediate_danger", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            dangerFlag = d.TryGetProperty("flag", out var f) && f.ValueKind == JsonValueKind.True;
            // A model flag raises the alert even without a quote: missing a danger costs more than a false alarm.
            if (dangerFlag && Str(d, "quote") is not null) dangerEvidence = Ground(d);
        }

        var extra = new List<ExtraPersonalData>();
        foreach (var item in Array(root, "personal_data").Take(50))
        {
            var text = Str(item, "text");
            var type = Str(item, "type");
            if (text is null || type is null || !PersonalDataType.All.Contains(type)) continue;
            // The model saw placeholders; what it lists is looked up in the original text.
            extra.Add(new ExtraPersonalData(seen.Restore(text), type));
        }

        return new Parsed(terms, fields, dangerFlag, dangerEvidence, extra, returned, grounded, dropped);
    }

    private static IEnumerable<JsonElement> Array(JsonElement root, string name) =>
        root.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object) : [];

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : null;
}
