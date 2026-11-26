using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Testigo.Support;

namespace Testigo.Triage;

/// <summary>
/// Text folded for matching (lower case, no accents, typographic quotes and dashes made plain,
/// runs of whitespace collapsed), with a map from every folded character back to its position in
/// the original, so a match can be shown highlighted in the original text.
/// </summary>
public sealed class FoldedText
{
    private readonly int[] origin;

    private FoldedText(string original, string folded, int[] origin)
    {
        Original = original;
        Folded = folded;
        this.origin = origin;
    }

    public string Original { get; }
    public string Folded { get; }

    public static FoldedText Of(string text)
    {
        var sb = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        var lastWasSpace = true;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    map.Add(i);
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            var plain = c switch
            {
                '“' or '”' or '«' or '»' or '„' => "\"",
                '‘' or '’' or '´' or '`' => "'",
                '–' or '—' or '‐' => "-",
                '…' => "...",
                _ => c.ToString().Normalize(NormalizationForm.FormD),
            };
            foreach (var p in plain)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(p) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.ToLowerInvariant(p));
                map.Add(i);
            }
        }

        while (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
            map.RemoveAt(map.Count - 1);
        }

        return new FoldedText(text, sb.ToString(), [.. map]);
    }

    /// <summary>The original range behind a folded range.</summary>
    public (int Start, int Length) ToOriginal(int foldedStart, int foldedLength)
    {
        var start = origin[foldedStart];
        var last = origin[foldedStart + foldedLength - 1];
        // A folded character may stand for a whole original character (e.g. "é" → "e").
        var end = last + 1;
        return (start, end - start);
    }
}

/// <summary>Where a quote was found in a text.</summary>
public sealed record QuoteLocation(int Start, int Length)
{
    public string In(string text) => text.Substring(Start, Length);
}

/// <summary>
/// Finds a model's quote in the text it was given (FR-ANA-4, NFR-AI-4). The quote must be there
/// word for word; only case, accents, quote marks and whitespace may differ. Anything else counts
/// as not found, and the suggestion that carried it is dropped.
/// </summary>
public static class QuoteLocator
{
    /// <summary>Quotes shorter than this (after folding) prove nothing and are refused.</summary>
    public const int MinimumLength = 8;
    public const int MaximumLength = 600;

    public static QuoteLocation? Find(string text, string? quote)
    {
        if (string.IsNullOrWhiteSpace(quote)) return null;
        var q = FoldedText.Of(quote.Trim().Trim('"', '“', '”', '«', '»').Trim());
        if (q.Folded.Length < MinimumLength || q.Folded.Length > MaximumLength) return null;
        var t = FoldedText.Of(text);
        var at = t.Folded.IndexOf(q.Folded, StringComparison.Ordinal);
        if (at < 0) return null;
        var (start, length) = t.ToOriginal(at, q.Folded.Length);
        return new QuoteLocation(start, length);
    }
}

/// <summary>Which language a report is written in (FR-ANA-1): es, pt, en, or und when unclear.</summary>
public static class LanguageDetector
{
    private static readonly string[] Spanish = ["el", "la", "los", "las", "que", "de", "y", "en", "un", "una", "por", "con", "para", "mi", "me", "es", "fue", "pero", "se", "lo", "del", "al", "muy", "porque", "cuando", "nos", "su", "hay", "sin", "esta", "este", "eso", "yo"];
    private static readonly string[] Portuguese = ["o", "a", "os", "as", "que", "de", "e", "em", "um", "uma", "por", "com", "para", "meu", "minha", "me", "foi", "mas", "se", "do", "da", "no", "na", "muito", "porque", "quando", "nos", "seu", "sua", "não", "nao", "isso", "eu", "ele", "ela", "também", "tambem", "você", "voce"];
    private static readonly string[] English = ["the", "and", "of", "to", "in", "a", "is", "was", "my", "me", "that", "it", "for", "with", "on", "they", "i", "but", "not", "this", "be", "have", "had", "were"];
    private static readonly Regex Word = new(@"\p{L}+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Detect(string text)
    {
        var words = Word.Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();
        if (words.Count < 3) return "und";
        int Score(string[] list) => words.Count(list.Contains);
        var scores = new[] { ("es", Score(Spanish)), ("pt", Score(Portuguese)), ("en", Score(English)) };
        // Spanish and Portuguese share many short words: decide on the ones that differ.
        var es = scores[0].Item2 + words.Count(w => w is "el" or "los" or "y" or "pero" or "muy" or "esta" or "yo" or "lo" or "del" or "hay");
        var pt = scores[1].Item2 + words.Count(w => w is "não" or "nao" or "você" or "voce" or "eu" or "meu" or "minha" or "também" or "tambem" or "isso" or "ele" or "ela" or "um" or "uma");
        var en = scores[2].Item2;
        var best = new[] { ("es", es), ("pt", pt), ("en", en) }.OrderByDescending(x => x.Item2).ToList();
        return best[0].Item2 == 0 || best[0].Item2 == best[1].Item2 ? "und" : best[0].Item1;
    }
}

/// <summary>The immediate-danger keyword rules (FR-ANA-6), loaded from data.</summary>
public sealed record DangerRuleSet
{
    public required string Version { get; init; }
    public string Note { get; init; } = string.Empty;
    public List<DangerRule> Rules { get; init; } = [];

    public static DangerRuleSet LoadDefault(string? overridePath = null) => TestigoData.Load<DangerRuleSet>("danger-rules-v1.json", overridePath);
}

public sealed record DangerRule
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public List<string> Patterns { get; init; } = [];
}

/// <summary>A rule that matched, with the sentence it matched in as evidence.</summary>
public sealed record DangerMatch(string RuleKey, string Label, int Start, int Length);

/// <summary>
/// Runs the keyword rules on a report's original text. It never depends on a model: either this
/// or the model check raises the immediate-danger flag (FR-ANA-6).
/// </summary>
public sealed class DangerDetector
{
    private readonly List<(DangerRule Rule, Regex Regex)> compiled;

    public DangerDetector(DangerRuleSet rules)
    {
        Version = rules.Version;
        compiled = [.. rules.Rules.SelectMany(r => r.Patterns.Select(p => (r, new Regex(p, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))))];
    }

    public string Version { get; }

    public IReadOnlyList<DangerMatch> Find(string text)
    {
        var folded = FoldedText.Of(text);
        var matches = new List<DangerMatch>();
        foreach (var (rule, regex) in compiled)
        {
            var m = regex.Match(folded.Folded);
            if (!m.Success || m.Length == 0) continue;
            if (matches.Any(x => x.RuleKey == rule.Key)) continue;
            var (start, length) = Sentence(text, folded.ToOriginal(m.Index, m.Length));
            matches.Add(new DangerMatch(rule.Key, rule.Label, start, length));
        }

        return matches;
    }

    /// <summary>The sentence around a match, so the evidence reads on its own.</summary>
    private static (int Start, int Length) Sentence(string text, (int Start, int Length) match)
    {
        var start = match.Start;
        while (start > 0 && ".!?\n".IndexOf(text[start - 1]) < 0) start--;
        while (start < match.Start && char.IsWhiteSpace(text[start])) start++;
        var end = match.Start + match.Length;
        while (end < text.Length && ".!?\n".IndexOf(text[end]) < 0) end++;
        if (end < text.Length && text[end] != '\n') end++;
        return (start, end - start);
    }
}
