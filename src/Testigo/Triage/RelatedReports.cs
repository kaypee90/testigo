using System.Text.RegularExpressions;

namespace Testigo.Triage;

/// <summary>How two cases may be related (data model §7.5, CaseLink.kind).</summary>
public static class LinkKind
{
    public const string Duplicate = "duplicate";
    public const string SameActor = "same_actor";
    public const string SameTechnology = "same_technology";
    public const string Related = "related";
}

/// <summary>What the related-report search knows about a report: only its redacted copy and its suggested terms.</summary>
public sealed record RelatedCandidate(Guid CaseId, string RedactedText, IReadOnlyCollection<string> TechnologyTerms, IReadOnlyCollection<string> ActorNames);

public sealed record RelatedSuggestion(Guid CaseId, string Kind, double Score);

/// <summary>
/// Suggests possibly related reports (FR-ANA-5) by comparing redacted copies (bag-of-words
/// cosine, placeholders and short words ignored) and shared actor or technology. It only suggests:
/// an analyst confirms each link, and reports are never merged automatically. Phase 0 runs it in
/// memory over the organization's recent reports; hybrid search with pgvector replaces it later.
/// </summary>
public static class RelatedReports
{
    public const double DuplicateThreshold = 0.72;
    public const double RelatedThreshold = 0.38;
    public const double SameTechnologyThreshold = 0.18;
    public const int MaxSuggestions = 5;

    private static readonly Regex Word = new(@"\p{L}{4,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly HashSet<string> Stop = ["para", "pero", "como", "porque", "cuando", "donde", "esta", "este", "esto", "estos", "estas", "tiene", "tenia", "tengo", "hace", "hizo", "fueron", "eran", "sobre", "entre", "desde", "hasta", "todo", "todos", "toda", "todas", "mismo", "misma", "otra", "otro", "otros", "nada", "algo", "ellos", "ellas", "nosotros", "ustedes", "nuestra", "nuestro", "despues", "antes", "luego", "solo", "tambien", "mucho", "mucha", "muy", "cada", "porque", "entonces", "aunque", "sino", "pues", "ademas", "aqui", "alli", "ahora", "siempre", "nunca", "parece", "dijo", "dicen", "decir", "estaba", "estoy", "estamos", "fue", "habia", "hemos", "hubo", "puede", "pueden", "quiero", "quiere", "mios", "suyo", "suya", "nossa", "nosso", "isso", "esse", "essa", "mais", "quando", "porque", "muito", "tambem", "depois"];

    public static IReadOnlyList<RelatedSuggestion> Suggest(RelatedCandidate report, IEnumerable<RelatedCandidate> others)
    {
        var mine = Vector(report.RedactedText);
        var actors = report.ActorNames.Select(Fold).Where(a => a.Length >= 3).ToHashSet(StringComparer.Ordinal);
        var results = new List<RelatedSuggestion>();
        foreach (var other in others)
        {
            if (other.CaseId == report.CaseId) continue;
            var score = Cosine(mine, Vector(other.RedactedText));
            var sameActor = actors.Count > 0 && other.ActorNames.Select(Fold).Any(actors.Contains);
            var sameTechnology = report.TechnologyTerms.Intersect(other.TechnologyTerms).Any(t => t is not "other");
            string? kind =
                score >= DuplicateThreshold ? LinkKind.Duplicate :
                sameActor ? LinkKind.SameActor :
                sameTechnology && score >= SameTechnologyThreshold ? LinkKind.SameTechnology :
                score >= RelatedThreshold ? LinkKind.Related :
                null;
            if (kind is not null) results.Add(new RelatedSuggestion(other.CaseId, kind, Math.Round(score, 3)));
        }

        return [.. results.OrderBy(r => Rank(r.Kind)).ThenByDescending(r => r.Score).Take(MaxSuggestions)];
    }

    private static int Rank(string kind) => kind switch
    {
        LinkKind.Duplicate => 0,
        LinkKind.SameActor => 1,
        LinkKind.SameTechnology => 2,
        _ => 3,
    };

    private static Dictionary<string, int> Vector(string text)
    {
        var folded = FoldedText.Of(Redaction.RedactionResult.Placeholder.Replace(text, " ")).Folded;
        var v = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Word.Matches(folded))
        {
            var w = m.Value;
            if (Stop.Contains(w)) continue;
            // A light stem: plural and gender endings don't make words different.
            if (w.Length > 5 && (w.EndsWith("es") || w.EndsWith("as") || w.EndsWith("os"))) w = w[..^2];
            else if (w.Length > 4 && (w.EndsWith('s') || w.EndsWith('a') || w.EndsWith('o'))) w = w[..^1];
            v[w] = v.GetValueOrDefault(w) + 1;
        }

        return v;
    }

    private static double Cosine(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        double dot = 0;
        foreach (var (k, x) in a)
        {
            if (b.TryGetValue(k, out var y)) dot += x * y;
        }

        var na = Math.Sqrt(a.Values.Sum(x => (double)x * x));
        var nb = Math.Sqrt(b.Values.Sum(x => (double)x * x));
        return dot / (na * nb);
    }

    private static string Fold(string s) => FoldedText.Of(s).Folded;
}
