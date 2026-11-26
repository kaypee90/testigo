using Testigo.Support;
using Testigo.Taxonomy;

namespace Testigo.Forms;

/// <summary>
/// The public report form (FR-INT-1, FR-ADM-2): versioned data. Every report records the version
/// it was submitted with. The server validates every field against this definition; the reporter
/// app only renders it.
/// </summary>
public sealed record FormDefinition
{
    public required string Version { get; init; }
    public string Language { get; init; } = "es";
    public string Note { get; init; } = string.Empty;
    /// <summary>The version of the consent notice shown with this form (FR-INT-2).</summary>
    public required string NoticeVersion { get; init; }
    public List<FormField> Fields { get; init; } = [];
    public List<ConsentItem> Consents { get; init; } = [];
    public List<FormOption> ContactMethods { get; init; } = [];
    /// <summary>
    /// The same form in other languages (FR-INT-8): only the words change, never the fields,
    /// options or keys, so a report validates the same whatever language it was written in.
    /// </summary>
    public Dictionary<string, FormTranslation> Translations { get; init; } = [];

    /// <summary>The languages this form can be shown in, its own first.</summary>
    public IReadOnlyList<string> Languages => [Language, .. Translations.Keys.Where(k => k != Language)];

    public bool Supports(string? language) => language is not null && Languages.Contains(language);

    /// <summary>
    /// This form with its words in <paramref name="language"/> and that language's notice version;
    /// the form itself for its own language or one it doesn't have.
    /// </summary>
    public FormDefinition In(string? language)
    {
        if (language is null || language == Language || !Translations.TryGetValue(language, out var t)) return this;
        return this with
        {
            Language = language,
            NoticeVersion = t.NoticeVersion,
            Translations = [],
            Fields = [.. Fields.Select(f => f with
            {
                Label = t.Fields.GetValueOrDefault(f.Key)?.Label ?? f.Label,
                Help = t.Fields.GetValueOrDefault(f.Key) is { } ft ? ft.Help : f.Help,
                Options = [.. f.Options.Select(o => o with { Label = t.OptionLabel(f.Key, o.Key) ?? o.Label })],
                ExtraOptions = [.. f.ExtraOptions.Select(o => o with { Label = t.OptionLabel(f.Key, o.Key) ?? o.Label })],
            })],
            Consents = [.. Consents.Select(c => c with { Label = t.Consents.GetValueOrDefault(c.Key) ?? c.Label })],
            ContactMethods = [.. ContactMethods.Select(m => m with { Label = t.ContactMethods.GetValueOrDefault(m.Key) ?? m.Label })],
        };
    }

    /// <summary>The field that holds the report's free text.</summary>
    public const string FreeTextField = "what_happened";

    public FormField? Field(string key) => Fields.FirstOrDefault(f => f.Key == key);

    /// <summary>A field's choices: its own options, or the taxonomy terms it's bound to plus any extras.</summary>
    public IReadOnlyList<FormOption> OptionsFor(FormField field, HarmTaxonomy taxonomy, string language = "es")
    {
        if (field.Taxonomy is null) return field.Options;
        var dimension = taxonomy.Dimension(field.Taxonomy) ?? throw new InvalidOperationException($"Field '{field.Key}' uses unknown dimension '{field.Taxonomy}'.");
        return [.. dimension.Terms.Select(t => new FormOption { Key = t.Key, Label = t.Labels.GetValueOrDefault(language) ?? t.Labels.GetValueOrDefault("es") ?? t.Key }), .. field.ExtraOptions];
    }

    /// <summary>Problems that make the form unusable with this taxonomy; empty when it's valid.</summary>
    public IReadOnlyList<string> Validate(HarmTaxonomy taxonomy)
    {
        var problems = new List<string>();
        if (Field(FreeTextField) is not { Kind: FieldKind.LongText, Required: true }) problems.Add($"The form needs a required long-text field '{FreeTextField}'.");
        foreach (var group in Fields.GroupBy(f => f.Key).Where(g => g.Count() > 1)) problems.Add($"Field '{group.Key}' appears more than once.");
        foreach (var f in Fields)
        {
            if (f.Taxonomy is not null && taxonomy.Dimension(f.Taxonomy) is null) problems.Add($"Field '{f.Key}' uses unknown dimension '{f.Taxonomy}'.");
            if (f.Kind is FieldKind.Choice or FieldKind.MultiChoice && f.Taxonomy is null && f.Options.Count == 0) problems.Add($"Field '{f.Key}' has no options.");
            if (f.OnlyWhen is { } when && when.Keys.Any(k => Field(k) is null)) problems.Add($"Field '{f.Key}' depends on an unknown field.");
        }

        if (!Consents.Any(c => c is { Key: ConsentItem.Processing, Required: true })) problems.Add("The form needs the required 'processing' consent.");

        // A translation covers every word a reporter reads, so a page never switches language
        // halfway down, and it brings its own notice version, so consent records which text was shown.
        foreach (var (language, t) in Translations)
        {
            if (string.IsNullOrWhiteSpace(t.NoticeVersion) || t.NoticeVersion == NoticeVersion) problems.Add($"Translation '{language}' needs a notice_version of its own.");
            foreach (var key in t.Fields.Keys.Where(k => Field(k) is null)) problems.Add($"Translation '{language}' names unknown field '{key}'.");
            foreach (var f in Fields)
            {
                if (t.Fields.GetValueOrDefault(f.Key) is not { } ft) { problems.Add($"Translation '{language}' is missing field '{f.Key}'."); continue; }
                if (f.Help is not null && ft.Help is null) problems.Add($"Translation '{language}' is missing the help of '{f.Key}'.");
                if (f.Taxonomy is not null && taxonomy.Dimension(f.Taxonomy) is { } d && d.Terms.Any(term => !term.Labels.ContainsKey(language)))
                    problems.Add($"The taxonomy has no '{language}' labels for '{f.Taxonomy}'.");
                foreach (var o in f.Options.Concat(f.ExtraOptions).Where(o => t.OptionLabel(f.Key, o.Key) is null))
                    problems.Add($"Translation '{language}' is missing option '{f.Key}.{o.Key}'.");
            }

            foreach (var c in Consents.Where(c => !t.Consents.ContainsKey(c.Key))) problems.Add($"Translation '{language}' is missing consent '{c.Key}'.");
            foreach (var m in ContactMethods.Where(m => !t.ContactMethods.ContainsKey(m.Key))) problems.Add($"Translation '{language}' is missing contact method '{m.Key}'.");
        }

        return problems;
    }

    public static FormDefinition LoadDefault(HarmTaxonomy taxonomy, string? overridePath = null)
    {
        var form = TestigoData.Load<FormDefinition>("form-v1.json", overridePath);
        var problems = form.Validate(taxonomy);
        if (problems.Count > 0) throw new InvalidOperationException("The report form isn't valid: " + string.Join(" ", problems));
        return form;
    }
}

public enum FieldKind
{
    Text,
    LongText,
    Choice,
    MultiChoice
}

public sealed record FormField
{
    public required string Key { get; init; }
    public required FieldKind Kind { get; init; }
    public bool Required { get; init; }
    public int MaxLength { get; init; } = 2000;
    public required string Label { get; init; }
    public string? Help { get; init; }
    /// <summary>The taxonomy dimension whose terms are the options.</summary>
    public string? Taxonomy { get; init; }
    public List<FormOption> Options { get; init; } = [];
    public List<FormOption> ExtraOptions { get; init; } = [];
    public string? Default { get; init; }
    /// <summary>Asked only when other answers have these values (e.g. region only for Chile).</summary>
    public Dictionary<string, string>? OnlyWhen { get; init; }
}

/// <summary>The words of a form in another language. Keys are the form's own keys.</summary>
public sealed record FormTranslation
{
    /// <summary>Whether a person has reviewed it (FR-INT-8: never machine-only in real use).</summary>
    public string Status { get; init; } = string.Empty;
    /// <summary>The consent notice in this language has its own version, recorded with each report.</summary>
    public required string NoticeVersion { get; init; }
    public Dictionary<string, FieldText> Fields { get; init; } = [];
    /// <summary>Field key to option key to label, for options the form lists itself (not taxonomy terms).</summary>
    public Dictionary<string, Dictionary<string, string>> Options { get; init; } = [];
    public Dictionary<string, string> Consents { get; init; } = [];
    public Dictionary<string, string> ContactMethods { get; init; } = [];

    public string? OptionLabel(string field, string option) => Options.GetValueOrDefault(field)?.GetValueOrDefault(option);
}

public sealed record FieldText
{
    public required string Label { get; init; }
    public string? Help { get; init; }
}

public sealed record FormOption
{
    public required string Key { get; init; }
    public required string Label { get; init; }
}

public sealed record ConsentItem
{
    public const string Processing = "processing";
    public const string Contact = "contact";
    public const string Publication = "publication";

    public required string Key { get; init; }
    public bool Required { get; init; }
    public required string Label { get; init; }
}

/// <summary>What a reporter (or the reporter app on their behalf) sends. Untrusted.</summary>
public sealed record ReportSubmission
{
    public string? FormVersion { get; init; }
    /// <summary>Field key to its values: one for text fields and single choices, any number for multiple choice.</summary>
    public Dictionary<string, List<string>> Answers { get; init; } = [];
    /// <summary>The consent keys the reporter ticked. Nothing is ticked by default (FR-INT-2).</summary>
    public List<string> Consents { get; init; } = [];
    public string? ContactMethod { get; init; }
    public string? ContactValue { get; init; }
    /// <summary>A passphrase the reporter chose; null to have one generated (FR-INT-6).</summary>
    public string? Passphrase { get; init; }
    /// <summary>The language the form was shown in: it picks the notice version recorded and the
    /// language of any error messages. The form's own language when missing or unknown.</summary>
    public string? Language { get; init; }
}

/// <summary>A submission that passed validation, normalized.</summary>
public sealed record ValidatedReport
{
    public required string FormVersion { get; init; }
    public required string NoticeVersion { get; init; }
    /// <summary>The language the form was shown in (not necessarily the one the report is written in).</summary>
    public required string FormLanguage { get; init; }
    public required string FreeText { get; init; }
    /// <summary>Every other answered field (choices and short text). Encrypted at rest.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Answers { get; init; }
    public required bool ConsentToContact { get; init; }
    public required bool ConsentToPublication { get; init; }
    public string? ContactMethod { get; init; }
    public string? ContactValue { get; init; }
    public string? Country => Answers.GetValueOrDefault("country")?.FirstOrDefault();
    public string? Region => Answers.GetValueOrDefault("region")?.FirstOrDefault();
}

/// <summary>A field's problem, in the language the form was shown in, shown next to the field.</summary>
public sealed record FieldError(string Field, string Message);

/// <summary>Server-side validation of every field (FR-INT-1), like the runtime's form triggers.</summary>
public static class ReportValidator
{
    public const int MaxContactLength = 200;

    public static (ValidatedReport? Report, IReadOnlyList<FieldError> Errors) Validate(FormDefinition form, HarmTaxonomy taxonomy, ReportSubmission submission)
    {
        var errors = new List<FieldError>();
        form = form.In(form.Supports(submission.Language) ? submission.Language : form.Language);
        var m = ValidationMessages.For(form.Language);
        if (submission.FormVersion is { } v && v != form.Version)
        {
            errors.Add(new FieldError("form", m.FormChanged));
            return (null, errors);
        }

        var unknown = submission.Answers.Keys.Where(k => form.Field(k) is null).ToList();
        foreach (var key in unknown) errors.Add(new FieldError(key, m.UnknownField));

        var answers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var field in form.Fields)
        {
            var values = (submission.Answers.GetValueOrDefault(field.Key) ?? [])
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => x.Length > 0)
                .ToList();
            if (field.OnlyWhen is { } when && !when.All(c => answers.GetValueOrDefault(c.Key)?.FirstOrDefault() == c.Value))
            {
                continue; // not asked, so not kept
            }

            if (values.Count == 0)
            {
                if (field.Required) errors.Add(new FieldError(field.Key, m.Required));
                continue;
            }

            switch (field.Kind)
            {
                case FieldKind.Text or FieldKind.LongText:
                    if (values.Count > 1) { errors.Add(new FieldError(field.Key, m.OneText)); continue; }
                    var text = values[0].Replace("\r\n", "\n");
                    if (text.Length > field.MaxLength) { errors.Add(new FieldError(field.Key, m.TooLong(field.MaxLength))); continue; }
                    answers[field.Key] = [text];
                    break;
                case FieldKind.Choice or FieldKind.MultiChoice:
                    var options = form.OptionsFor(field, taxonomy).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
                    var distinct = values.Distinct(StringComparer.Ordinal).ToList();
                    if (field.Kind == FieldKind.Choice && distinct.Count > 1) { errors.Add(new FieldError(field.Key, m.OneOption)); continue; }
                    if (distinct.Any(x => !options.Contains(x))) { errors.Add(new FieldError(field.Key, m.UnknownOption)); continue; }
                    answers[field.Key] = distinct;
                    break;
            }
        }

        var consents = submission.Consents.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        foreach (var c in consents.Where(c => form.Consents.All(x => x.Key != c))) errors.Add(new FieldError("consents", m.UnknownConsent));
        foreach (var c in form.Consents.Where(c => c.Required && !consents.Contains(c.Key))) errors.Add(new FieldError($"consent_{c.Key}", m.ConsentNeeded));

        var consentToContact = consents.Contains(ConsentItem.Contact);
        string? method = string.IsNullOrWhiteSpace(submission.ContactMethod) ? null : submission.ContactMethod.Trim();
        string? value = string.IsNullOrWhiteSpace(submission.ContactValue) ? null : submission.ContactValue.Trim();
        if (consentToContact)
        {
            if (method is null || form.ContactMethods.All(m => m.Key != method)) errors.Add(new FieldError("contact_method", m.ContactMethod));
            if (value is null) errors.Add(new FieldError("contact_value", m.ContactValue));
            else if (value.Length > MaxContactLength) errors.Add(new FieldError("contact_value", m.TooLong(MaxContactLength)));
        }
        else
        {
            // Without consent to be contacted, contact details are never kept (FR-INT-3).
            method = null;
            value = null;
        }

        if (errors.Count > 0) return (null, errors);
        var freeText = answers[FormDefinition.FreeTextField][0];
        answers.Remove(FormDefinition.FreeTextField);
        return (new ValidatedReport
        {
            FormVersion = form.Version,
            NoticeVersion = form.NoticeVersion,
            FormLanguage = form.Language,
            FreeText = freeText,
            Answers = answers,
            ConsentToContact = consentToContact,
            ConsentToPublication = consents.Contains(ConsentItem.Publication),
            ContactMethod = method,
            ContactValue = value,
        }, []);
    }
}

/// <summary>
/// The validator's messages, shown to reporters next to a field. Spanish first; a language without
/// its own messages gets Spanish. Like every reporter-facing text, a translation needs review by a
/// person before real use (FR-INT-8).
/// </summary>
public sealed record ValidationMessages
{
    public required string FormChanged { get; init; }
    public required string UnknownField { get; init; }
    public required string Required { get; init; }
    public required string OneText { get; init; }
    public required Func<int, string> TooLong { get; init; }
    public required string OneOption { get; init; }
    public required string UnknownOption { get; init; }
    public required string UnknownConsent { get; init; }
    public required string ConsentNeeded { get; init; }
    public required string ContactMethod { get; init; }
    public required string ContactValue { get; init; }

    public static readonly ValidationMessages Spanish = new()
    {
        FormChanged = "El formulario cambió. Vuelve a cargar la página.",
        UnknownField = "Este campo no existe en el formulario.",
        Required = "Este campo es necesario.",
        OneText = "Se esperaba un solo texto.",
        TooLong = max => $"Usa como máximo {max} caracteres.",
        OneOption = "Elige una sola opción.",
        UnknownOption = "Elige una de las opciones.",
        UnknownConsent = "Hay un consentimiento que no existe.",
        ConsentNeeded = "Necesitamos esta confirmación para recibir el reporte.",
        ContactMethod = "Elige cómo quieres que te contacten.",
        ContactValue = "Escribe el dato de contacto, o desmarca la opción de contacto.",
    };

    /// <summary>Draft English, not yet reviewed by a person (FR-INT-8).</summary>
    public static readonly ValidationMessages English = new()
    {
        FormChanged = "The form has changed. Please reload the page.",
        UnknownField = "This field isn't part of the form.",
        Required = "This field is needed.",
        OneText = "Only one answer was expected.",
        TooLong = max => $"Please use {max} characters at most.",
        OneOption = "Choose just one option.",
        UnknownOption = "Choose one of the options.",
        UnknownConsent = "One of the permissions doesn't exist.",
        ConsentNeeded = "We need this confirmation to receive the report.",
        ContactMethod = "Choose how you'd like to be contacted.",
        ContactValue = "Write your contact details, or untick the contact option.",
    };

    public static ValidationMessages For(string? language) => language == "en" ? English : Spanish;
}
