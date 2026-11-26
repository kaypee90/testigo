using System.Reflection;
using System.Text.Json;

namespace Testigo.Reporter;

/// <summary>
/// Every text a reporter reads in one language, from a translation file (FR-INT-8). Spanish
/// (Chilean usage) first; other languages are added as files reviewed by people, never
/// machine-only translations in real use.
/// </summary>
public sealed class Texts
{
    private readonly Dictionary<string, string> texts;

    public Texts(string language = "es")
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName(language))
            ?? throw new InvalidOperationException($"No reporter texts for '{language}'.");
        texts = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        Language = language;
    }

    public string Language { get; }

    public string this[string key] => texts.TryGetValue(key, out var t) ? t : throw new KeyNotFoundException($"Missing reporter text '{key}'.");

    public IReadOnlyCollection<string> Keys => texts.Keys;

    internal static string ResourceName(string language) => $"Testigo.Reporter.Texts.{language}.json";

    /// <summary>The languages that have a texts file in this build.</summary>
    public static IReadOnlyList<string> Available { get; } = Assembly.GetExecutingAssembly().GetManifestResourceNames()
        .Where(n => n.StartsWith("Testigo.Reporter.Texts.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
        .Select(n => n["Testigo.Reporter.Texts.".Length..^".json".Length])
        .Order(StringComparer.Ordinal)
        .ToList();
}

/// <summary>
/// The languages this reporter app offers (Reporter:Languages, VALIDATE), Spanish first and the
/// default. Spanish pages are at the root; every other language under its code (/en, /en/reportar),
/// so the language is in the address: no cookie and no JavaScript (NFR-SEC-2, NFR-SEC-6).
/// </summary>
public sealed class TextCatalog
{
    public const string Default = "es";
    private readonly Dictionary<string, Texts> byLanguage;

    public TextCatalog(IEnumerable<string>? enabled)
    {
        var languages = (enabled ?? Texts.Available).Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0).Prepend(Default).Distinct().ToList();
        var missing = languages.Where(l => !Texts.Available.Contains(l)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"Reporter:Languages names languages with no texts file: {string.Join(", ", missing)}.");
        byLanguage = languages.ToDictionary(l => l, l => new Texts(l));
        Languages = languages;
    }

    /// <summary>The offered languages, Spanish first.</summary>
    public IReadOnlyList<string> Languages { get; }

    public Texts Spanish => byLanguage[Default];

    public Texts For(string? language) => language is not null && byLanguage.TryGetValue(language, out var t) ? t : Spanish;

    /// <summary>The address of <paramref name="path"/> (without any language prefix) in <paramref name="language"/>.</summary>
    public static string PathIn(string language, string path) =>
        language == Default ? (string.IsNullOrEmpty(path) ? "/" : path) : "/" + language + (path is "" or "/" ? "" : path);
}

/// <summary>
/// Reads the language from the start of the address (/en/reportar) and moves that part into the
/// path base, so the pages, their links (~/) and the antiforgery cookie all work the same in every
/// language. Runs first, so error pages are shown in the same language.
/// </summary>
public sealed class LanguageMiddleware(RequestDelegate next, TextCatalog catalog)
{
    public const string ItemKey = "testigo.language";
    private const string OuterBaseKey = "testigo.outer-path-base";

    public Task InvokeAsync(HttpContext http)
    {
        var language = TextCatalog.Default;
        http.Items[OuterBaseKey] = http.Request.PathBase;
        foreach (var l in catalog.Languages.Where(l => l != TextCatalog.Default))
        {
            if (!http.Request.Path.StartsWithSegments("/" + l, out var rest)) continue;
            http.Request.PathBase = http.Request.PathBase.Add("/" + l);
            http.Request.Path = rest.HasValue ? rest : "/";
            language = l;
            break;
        }

        http.Items[ItemKey] = language;
        return next(http);
    }

    public static string LanguageOf(HttpContext? http) => http?.Items[ItemKey] as string ?? TextCatalog.Default;

    /// <summary>The address of the current page in <paramref name="language"/>, without its query string.</summary>
    public static string AddressIn(HttpContext http, string language) =>
        (http.Items[OuterBaseKey] is PathString outer ? outer : PathString.Empty).Value + TextCatalog.PathIn(language, http.Request.Path.Value ?? "/");
}
