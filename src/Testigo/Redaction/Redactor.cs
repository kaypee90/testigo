using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Testigo.Redaction;

/// <summary>The kinds of personal data replaced in analysis copies (FR-ANA-3).</summary>
public static class PersonalDataType
{
    public const string Person = "PERSON";
    public const string Rut = "RUT";
    public const string IdNumber = "ID";
    public const string Email = "EMAIL";
    public const string Phone = "PHONE";
    public const string Url = "URL";
    public const string Handle = "HANDLE";
    public const string Address = "ADDRESS";
    public const string Location = "LOCATION";

    public static readonly IReadOnlyList<string> All = [Person, Rut, IdNumber, Email, Phone, Url, Handle, Address, Location];

    /// <summary>How the reporter form names each kind when it warns (FR-INT-3).</summary>
    public static string Spanish(string type) => type switch
    {
        Person => "nombres de personas",
        Rut => "un RUT",
        IdNumber => "números de identificación",
        Email => "correos electrónicos",
        Phone => "números de teléfono",
        Url => "enlaces",
        Handle => "nombres de usuario",
        Address => "direcciones",
        Location => "ubicaciones exactas",
        _ => "datos personales",
    };

    /// <summary>The same names in English (a draft, FR-INT-8), for the warning on the English form.</summary>
    public static string English(string type) => type switch
    {
        Person => "people's names",
        Rut => "a RUT (Chilean ID number)",
        IdNumber => "ID numbers",
        Email => "email addresses",
        Phone => "phone numbers",
        Url => "links",
        Handle => "usernames",
        Address => "addresses",
        Location => "exact locations",
        _ => "personal data",
    };

    /// <summary>The name of a kind of personal data, for reporters, in their language.</summary>
    public static string Describe(string type, string? language) => language == "en" ? English(type) : Spanish(type);
}

/// <summary>One replaced piece of text: where it was in the original and where its placeholder is.</summary>
public sealed record RedactionSpan(int Start, int Length, string Type, string Placeholder, string Original, int RedactedStart);

/// <summary>A redacted copy: the text agents and models see, and the map back to the original.</summary>
public sealed record RedactionResult
{
    public required string Original { get; init; }
    public required string Text { get; init; }
    public required IReadOnlyList<RedactionSpan> Spans { get; init; }
    /// <summary>Placeholder to original value. Stored encrypted, shown only to analysts.</summary>
    public required IReadOnlyDictionary<string, string> Map { get; init; }
    public required string MethodVersion { get; init; }

    public IReadOnlyList<string> Types => Spans.Select(s => s.Type).Distinct().ToList();

    /// <summary>Puts the original values back into a piece of the redacted text.</summary>
    public string Restore(string redactedFragment)
    {
        if (Map.Count == 0) return redactedFragment;
        return Placeholder.Replace(redactedFragment, m => Map.TryGetValue(m.Value, out var v) ? v : m.Value);
    }

    /// <summary>
    /// The original-text range a range of the redacted text came from. A range that touches a
    /// placeholder covers the whole original value behind it.
    /// </summary>
    public (int Start, int Length) ToOriginal(int redactedStart, int redactedLength)
    {
        int Map1(int pos, bool end)
        {
            var shift = 0;
            foreach (var s in Spans)
            {
                var rEnd = s.RedactedStart + s.Placeholder.Length;
                if (pos < s.RedactedStart) break;
                if (pos < rEnd || (end && pos == rEnd))
                {
                    // Inside a placeholder: snap to the original value's start or end.
                    return end ? s.Start + s.Length : s.Start;
                }

                shift += s.Length - s.Placeholder.Length;
            }

            return pos + shift;
        }

        var start = Map1(redactedStart, end: false);
        var finish = Map1(redactedStart + redactedLength, end: true);
        return (start, Math.Max(0, finish - start));
    }

    internal static readonly Regex Placeholder = new(@"\[(?:PERSON|RUT|ID|EMAIL|PHONE|URL|HANDLE|ADDRESS|LOCATION)_\d+\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}

/// <summary>Extra values to redact, found by the model-assisted step (FR-ANA-3).</summary>
public sealed record ExtraPersonalData(string Text, string Type);

/// <summary>
/// The deterministic redaction step (FR-ANA-3, §7.2 item 4): rules for RUTs, ID numbers, emails,
/// phones, URLs, handles, addresses, coordinates and names from a gazetteer. The same value always
/// gets the same placeholder in one report. Values the model-assisted step finds are passed back
/// in as <see cref="ExtraPersonalData"/> and replaced the same way, so the final copy is always
/// built from the original text by code, never written by a model.
/// </summary>
public sealed class Redactor
{
    public const string RulesVersion = "rules-v1";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly Regex Email = new(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", Options, Timeout);
    private static readonly Regex Url = new(@"\b(?:https?://|www\.)[^\s<>""']+", Options, Timeout);
    private static readonly Regex Handle = new(@"(?<![\w@.])@[A-Z0-9_]{3,30}\b", Options, Timeout);
    // 12.345.678-9, 12345678-9, 9.876.543-K (shapes only; any check digit), also with spaces around the dash.
    private static readonly Regex RutShaped = new(@"\b\d{1,2}(?:\.\d{3}){2}\s?-\s?[\dK]\b|\b\d{7,8}\s?-\s?[\dK]\b", Options, Timeout);
    // "RUT 12345678", "run: 12.345.678" without the check digit.
    private static readonly Regex RutLabelled = new(@"\b(?:RUT|RUN)\b\s*(?:n[°º.]?\s*)?:?\s*(\d{1,2}\.?\d{3}\.?\d{3}(?:\s?-\s?[\dK])?)", Options, Timeout);
    private static readonly Regex Phone = new(
        @"(?:\+\s?\d{2,3}[\s.\-]?)?\(?\d{1,3}\)?[\s.\-]?\d{3,5}[\s.\-]?\d{3,5}\b",
        Options, Timeout);
    // "Mi pasaporte es AB1234567", "cédula n° 12.345.678", "DNI: 30111222". The value must contain a
    // digit, so "pasaporte vencido" keeps its word.
    private static readonly Regex IdLabelled = new(@"\b(?:pasaporte|passport|c[ée]dula|CI|DNI|CPF|RG|folio|n[uú]mero de (?:cuenta|tarjeta|documento|ficha))\b(?:\s+(?:es|era|n[uú]mero|nro\.?|n[°º.]))*\s*(?:n[°º.]?\s*)?:?\s*((?=[A-Z.\-]*\d)[A-Z0-9][A-Z0-9.\-]{4,20})", Options, Timeout);
    private static readonly Regex LongNumber = new(@"\b\d[\d.\-]{6,}\d\b", Options, Timeout);
    private static readonly Regex Coordinates = new(@"-?\d{1,2}\.\d{3,}\s*,\s*-?\d{1,3}\.\d{3,}", Options, Timeout);
    private static readonly Regex Address = new(
        @"\b(?:calle|avenida|avda\.?|av\.|pasaje|psje\.?|camino|villa|poblaci[oó]n|condominio|block|depto\.?|departamento|rua|travessa|alameda)\s+(?:[\p{L}\d'.]+\s){0,5}?(?:#\s?|n[°º.]?\s?)?\d{1,5}(?:\s?,?\s?(?:depto\.?|dpto\.?|casa|of\.?|apto\.?)\s?\d{1,5})?",
        Options, Timeout);
    // "me llamo X", "mi nombre es X", "Sr. X", "doña X", "meu nome é X": the next one to three capitalized words.
    private static readonly Regex NameTrigger = new(
        @"\b(?i:me llamo|mi nombre es|soy la|soy el|mi (?:hij[oa]|herman[oa]|madre|padre|mam[aá]|pap[aá]|espos[oa]|pareja|jef[ea]|vecin[oa]|compa[ñn]er[oa]|prim[oa]|t[ií][oa]|sobrin[oa]|abuel[oa]) se llama|sr\.?|sra\.?|srta\.?|don|do[ñn]a|dr\.?|dra\.?|meu nome [ée]|me chamo|senhora?)\s+((?:\p{Lu}[\p{Ll}'\-]+(?:\s+|$)){1,3})",
        RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex CapitalizedRun = new(@"\p{Lu}[\p{Ll}'\-]+(?:\s+(?:de\s+(?:la\s+|los\s+)?|da\s+|dos\s+)?\p{Lu}[\p{Ll}'\-]+){0,3}", RegexOptions.CultureInvariant, Timeout);

    private static readonly Regex Word = new(@"[\p{L}'\-]+", RegexOptions.CultureInvariant, Timeout);

    private static readonly Lazy<HashSet<string>> Gazetteer = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Testigo.Data.nombres.txt")
            ?? throw new InvalidOperationException("The name gazetteer isn't embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => !w.StartsWith('#'))
            .Select(Fold)
            .ToHashSet(StringComparer.Ordinal);
    });

    /// <summary>Candidate spans, in priority order: an earlier rule wins an overlap of the same length.</summary>
    private static IEnumerable<(int Start, int Length, string Type)> Candidates(string text)
    {
        foreach (Match m in Email.Matches(text)) yield return (m.Index, m.Length, PersonalDataType.Email);
        foreach (Match m in Url.Matches(text)) yield return (m.Index, TrimTrailingPunctuation(m.Value), PersonalDataType.Url);
        foreach (Match m in RutShaped.Matches(text)) yield return (m.Index, m.Length, PersonalDataType.Rut);
        foreach (Match m in RutLabelled.Matches(text)) yield return (m.Groups[1].Index, m.Groups[1].Length, PersonalDataType.Rut);
        foreach (Match m in IdLabelled.Matches(text)) yield return (m.Groups[1].Index, m.Groups[1].Length, PersonalDataType.IdNumber);
        foreach (Match m in Coordinates.Matches(text)) yield return (m.Index, m.Length, PersonalDataType.Location);
        foreach (Match m in Address.Matches(text)) yield return (m.Index, m.Length, PersonalDataType.Address);
        foreach (Match m in Phone.Matches(text))
        {
            var digits = m.Value.Count(char.IsDigit);
            var before = text[Math.Max(0, m.Index - 2)..m.Index];
            if (digits is >= 8 and <= 13 && !before.Contains('$') && !LooksLikeYearOrAmount(m.Value)) yield return (m.Index, m.Length, PersonalDataType.Phone);
        }

        foreach (Match m in Handle.Matches(text)) yield return (m.Index, m.Length, PersonalDataType.Handle);
        foreach (Match m in LongNumber.Matches(text))
        {
            // Amounts of money ("$ 1.500.000", "1.500.000 pesos") are kept: they matter to the case.
            var before = text[Math.Max(0, m.Index - 2)..m.Index];
            var after = text[(m.Index + m.Length)..Math.Min(text.Length, m.Index + m.Length + 6)];
            if (before.Contains('$') || after.TrimStart().StartsWith("pesos", StringComparison.OrdinalIgnoreCase) || after.TrimStart().StartsWith("CLP", StringComparison.OrdinalIgnoreCase)) continue;
            if (m.Value.Count(char.IsDigit) >= 7) yield return (m.Index, m.Length, PersonalDataType.IdNumber);
        }

        foreach (Match m in NameTrigger.Matches(text))
        {
            var g = m.Groups[1];
            var value = g.Value.TrimEnd();
            yield return (g.Index, value.Length, PersonalDataType.Person);
        }

        foreach (Match m in CapitalizedRun.Matches(text))
        {
            // The run may start with a capitalized word that isn't a name ("Después Josefa Rojas"):
            // the name starts at the first word on the list and runs to the end of the run.
            foreach (Match w in Word.Matches(m.Value))
            {
                if (!char.IsUpper(w.Value[0]) || !Gazetteer.Value.Contains(Fold(w.Value))) continue;
                yield return (m.Index + w.Index, m.Length - w.Index, PersonalDataType.Person);
                break;
            }
        }
    }

    /// <summary>Redacts by rules, plus any extra values the model-assisted step found.</summary>
    public RedactionResult Redact(string original, IEnumerable<ExtraPersonalData>? extra = null)
    {
        var candidates = Candidates(original).ToList();
        var extras = (extra ?? []).Where(e => e.Text.Trim().Length >= 2 && PersonalDataType.All.Contains(e.Type)).ToList();
        foreach (var e in extras)
        {
            var value = e.Text.Trim();
            for (var i = original.IndexOf(value, StringComparison.Ordinal); i >= 0; i = original.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            {
                candidates.Add((i, value.Length, e.Type));
            }
        }

        // Longest first, then earliest, then rule order; drop anything overlapping a chosen span.
        var chosen = new List<(int Start, int Length, string Type)>();
        foreach (var c in candidates.Select((c, i) => (c, i)).OrderByDescending(x => x.c.Length).ThenBy(x => x.c.Start).ThenBy(x => x.i).Select(x => x.c))
        {
            if (c.Length <= 0) continue;
            if (chosen.Any(x => c.Start < x.Start + x.Length && x.Start < c.Start + c.Length)) continue;
            chosen.Add(c);
        }

        chosen.Sort((a, b) => a.Start.CompareTo(b.Start));
        var counters = new Dictionary<string, int>();
        var byValue = new Dictionary<(string, string), string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var spans = new List<RedactionSpan>();
        var sb = new StringBuilder(original.Length);
        var pos = 0;
        foreach (var (start, length, type) in chosen)
        {
            sb.Append(original, pos, start - pos);
            var value = original.Substring(start, length);
            var key = (type, NormalizeValue(type, value));
            if (!byValue.TryGetValue(key, out var placeholder))
            {
                var n = counters[type] = counters.GetValueOrDefault(type) + 1;
                placeholder = $"[{type}_{n}]";
                byValue[key] = placeholder;
                map[placeholder] = value;
            }

            spans.Add(new RedactionSpan(start, length, type, placeholder, value, sb.Length));
            sb.Append(placeholder);
            pos = start + length;
        }

        sb.Append(original, pos, original.Length - pos);
        return new RedactionResult
        {
            Original = original,
            Text = sb.ToString(),
            Spans = spans,
            Map = map,
            MethodVersion = extras.Count > 0 ? $"{RulesVersion}+model" : RulesVersion,
        };
    }

    /// <summary>The kinds of personal data in a text, for the reporter's warning before submission (FR-INT-3).</summary>
    public IReadOnlyList<string> Detect(string text) => Redact(text).Types;

    private static int TrimTrailingPunctuation(string url)
    {
        var end = url.Length;
        while (end > 0 && ".,;:!?)".Contains(url[end - 1])) end--;
        return end;
    }

    private static bool LooksLikeYearOrAmount(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        // "2024 2025", "$ 1.500.000" style amounts aren't phone numbers.
        return value.Contains('$') || (digits.Length == 8 && digits.StartsWith("20") && value.Contains(' ') && digits[4..].StartsWith("20"));
    }

    private static string NormalizeValue(string type, string value) => type switch
    {
        PersonalDataType.Phone or PersonalDataType.Rut or PersonalDataType.IdNumber => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(),
        _ => Fold(value),
    };

    /// <summary>Lower-case without accents, for comparing names.</summary>
    internal static string Fold(string s)
    {
        var d = s.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
