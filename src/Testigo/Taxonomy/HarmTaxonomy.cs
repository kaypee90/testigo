using Testigo.Support;

namespace Testigo.Taxonomy;

/// <summary>
/// The harm taxonomy (requirements §4): versioned data, not code. Every classification records
/// the version it used. The prototype ships a starting version (VALIDATE with the deploying organization).
/// </summary>
public sealed record HarmTaxonomy
{
    public required string Version { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    /// <summary>Global incident trackers each term may map to (§4.2): aiid, oecd_aim, aiaaic.</summary>
    public List<string> Trackers { get; init; } = [];
    public List<TaxonomyDimension> Dimensions { get; init; } = [];
    /// <summary>Analyst-set severities (dimension 7); never suggested by agents.</summary>
    public List<string> Severity { get; init; } = [];

    public TaxonomyDimension? Dimension(string key) => Dimensions.FirstOrDefault(d => d.Key == key);

    public TaxonomyTerm? Term(string dimension, string term) => Dimension(dimension)?.Terms.FirstOrDefault(t => t.Key == term);

    public bool Has(string dimension, string term) => Term(dimension, term) is not null;

    /// <summary>The term's label in the language asked for, falling back to Spanish, then the key.</summary>
    public string Label(string dimension, string term, string language = "es") =>
        Term(dimension, term) is { } t ? t.Labels.GetValueOrDefault(language) ?? t.Labels.GetValueOrDefault("es") ?? t.Key : term;

    /// <summary>Problems that make this taxonomy unusable; empty when it's valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Version)) problems.Add("The taxonomy needs a version.");
        if (Dimensions.Count == 0) problems.Add("The taxonomy has no dimensions.");
        foreach (var group in Dimensions.GroupBy(d => d.Key).Where(g => g.Count() > 1)) problems.Add($"Dimension '{group.Key}' appears more than once.");
        foreach (var d in Dimensions)
        {
            if (!IsKey(d.Key)) problems.Add($"Dimension key '{d.Key}' must be lower_snake_case.");
            if (!d.Labels.ContainsKey("es")) problems.Add($"Dimension '{d.Key}' needs a Spanish label.");
            if (d.Terms.Count == 0) problems.Add($"Dimension '{d.Key}' has no terms.");
            foreach (var group in d.Terms.GroupBy(t => t.Key).Where(g => g.Count() > 1)) problems.Add($"Term '{d.Key}.{group.Key}' appears more than once.");
            foreach (var t in d.Terms)
            {
                if (!IsKey(t.Key)) problems.Add($"Term key '{d.Key}.{t.Key}' must be lower_snake_case.");
                if (!t.Labels.ContainsKey("es")) problems.Add($"Term '{d.Key}.{t.Key}' needs a Spanish label.");
                foreach (var tracker in t.Mappings.Keys.Where(k => !Trackers.Contains(k))) problems.Add($"Term '{d.Key}.{t.Key}' maps to unknown tracker '{tracker}'.");
            }
        }

        return problems;
    }

    private static bool IsKey(string key) => key.Length is > 0 and <= 64 && key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    public static HarmTaxonomy LoadDefault(string? overridePath = null)
    {
        var taxonomy = TestigoData.Load<HarmTaxonomy>("taxonomy-v1.json", overridePath);
        var problems = taxonomy.Validate();
        if (problems.Count > 0) throw new InvalidOperationException("The taxonomy isn't valid: " + string.Join(" ", problems));
        return taxonomy;
    }
}

public sealed record TaxonomyDimension
{
    public required string Key { get; init; }
    public Dictionary<string, string> Labels { get; init; } = [];
    /// <summary>Whether a case can carry several terms of this dimension.</summary>
    public bool Multiple { get; init; }
    /// <summary>Optional dimensions (e.g. who was affected) can identify people.</summary>
    public bool Optional { get; init; }
    public string? Validate { get; init; }
    public List<TaxonomyTerm> Terms { get; init; } = [];
}

public sealed record TaxonomyTerm
{
    public required string Key { get; init; }
    public Dictionary<string, string> Labels { get; init; } = [];
    public Dictionary<string, string> Description { get; init; } = [];
    /// <summary>The equivalent term in each global tracker (§4.2); null until confirmed.</summary>
    public Dictionary<string, string?> Mappings { get; init; } = [];
}
