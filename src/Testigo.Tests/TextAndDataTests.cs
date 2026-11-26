using Testigo.Forms;
using Testigo.Support;
using Testigo.Taxonomy;
using Testigo.Triage;

namespace Testigo.Tests;

/// <summary>FR-ANA-4, NFR-AI-4: quotes must be in the text word for word.</summary>
public class QuoteLocatorTests
{
    private const string Text = "La “aplicación”   de Créditos Ficticios me negó el préstamo\nsin explicación.";

    [Theory]
    [InlineData("aplicación\" de créditos ficticios")]
    [InlineData("APLICACION\" DE CREDITOS FICTICIOS")]
    [InlineData("me nego el prestamo sin explicacion")]
    [InlineData("“me negó el préstamo sin explicación”")]
    public void Case_accents_quotes_and_spaces_do_not_matter(string quote)
    {
        var at = QuoteLocator.Find(Text, quote);
        Assert.NotNull(at);
        Assert.Equal(FoldedText.Of(quote.Trim('“', '”')).Folded, FoldedText.Of(at.In(Text)).Folded);
    }

    [Theory]
    [InlineData("me negaron el préstamo")]
    [InlineData("negó")]
    [InlineData("")]
    [InlineData(null)]
    public void Paraphrases_short_and_empty_quotes_are_refused(string? quote) => Assert.Null(QuoteLocator.Find(Text, quote));

    [Fact]
    public void The_location_covers_the_original_characters()
    {
        var at = QuoteLocator.Find(Text, "me nego el prestamo")!;
        Assert.Equal("me negó el préstamo", at.In(Text));
    }
}

public class LanguageDetectorTests
{
    [Theory]
    [InlineData("La empresa me negó el crédito y no me dijo por qué, pero yo pagué todo.", "es")]
    [InlineData("A empresa não me deu o crédito e eu não sei por que, isso foi muito ruim.", "pt")]
    [InlineData("The company denied my loan and I was not told why it was rejected.", "en")]
    [InlineData("ok", "und")]
    public void Detects_the_main_languages(string text, string expected) => Assert.Equal(expected, LanguageDetector.Detect(text));
}

/// <summary>FR-ANA-6: the immediate-danger keyword rules.</summary>
public class DangerDetectorTests
{
    [Theory]
    [InlineData("Después de lo que pasó ya no quiero vivir.", "self_harm")]
    [InlineData("Me dijeron que me van a matar si sigo reclamando.", "threat")]
    [InlineData("Mi hija de 12 años está en peligro por esa aplicación.", "child_at_risk")]
    [InlineData("Me golpearon cuando fui a reclamar.", "violence")]
    [InlineData("Eles disseram que vão me matar.", "threat")]
    public void Rules_find_danger_and_return_the_sentence(string text, string rule)
    {
        var match = Assert.Single(Fixtures.Danger.Find("Primera frase sin riesgo. " + text + " Otra frase."), m => m.RuleKey == rule);
        Assert.Equal(text, ("Primera frase sin riesgo. " + text + " Otra frase.").Substring(match.Start, match.Length));
    }

    [Fact]
    public void Ordinary_complaints_raise_nothing() =>
        Assert.Empty(Fixtures.Danger.Find("La aplicación me cobró de más y el soporte nunca respondió. Quiero que me devuelvan el dinero."));
}

/// <summary>FR-INT-1, FR-INT-2, FR-INT-3: the form is data and submissions are checked against it.</summary>
public class FormValidationTests
{
    private static readonly FormDefinition Form = FormDefinition.LoadDefault(Fixtures.Taxonomy);

    private static ReportSubmission Valid(Action<Dictionary<string, List<string>>>? edit = null, List<string>? consents = null, string? method = null, string? value = null)
    {
        var answers = new Dictionary<string, List<string>>
        {
            ["what_happened"] = ["Una app de selección de personal me descartó sin explicación."],
            ["technology"] = ["ai_automated_decision"],
            ["country"] = ["CL"],
            ["region"] = ["metropolitana"],
        };
        edit?.Invoke(answers);
        return new ReportSubmission { FormVersion = Form.Version, Answers = answers, Consents = consents ?? [ConsentItem.Processing], ContactMethod = method, ContactValue = value };
    }

    [Fact]
    public void The_default_form_and_taxonomy_are_consistent()
    {
        Assert.Empty(Fixtures.Taxonomy.Validate());
        Assert.Empty(Form.Validate(Fixtures.Taxonomy));
        Assert.Equal("es", Form.Language);
        Assert.Contains(Form.Consents, c => c.Key == ConsentItem.Processing && c.Required);
    }

    [Fact]
    public void The_english_form_has_every_word_translated_and_its_own_notice()
    {
        Assert.Equal(["es", "en"], Form.Languages);
        var english = Form.In("en");

        Assert.Equal("en", english.Language);
        Assert.NotEqual(Form.NoticeVersion, english.NoticeVersion);
        Assert.Equal(Form.Version, english.Version);
        Assert.Equal(Form.Fields.Select(f => f.Key), english.Fields.Select(f => f.Key));
        foreach (var (es, en) in Form.Fields.Zip(english.Fields))
        {
            Assert.NotEqual(es.Label, en.Label);
            Assert.Equal(es.Options.Select(o => o.Key), en.Options.Select(o => o.Key));
        }
        Assert.All(english.Consents.Zip(Form.Consents), p => Assert.NotEqual(p.Second.Label, p.First.Label));
        Assert.Same(Form, Form.In("pt"));
        Assert.Same(Form, Form.In(null));
    }

    [Fact]
    public void An_incomplete_translation_is_refused()
    {
        var en = Form.Translations["en"];
        var missingField = Form with { Translations = new() { ["en"] = en with { Fields = en.Fields.Where(f => f.Key != "what_happened").ToDictionary() } } };
        var sameNotice = Form with { Translations = new() { ["en"] = en with { NoticeVersion = Form.NoticeVersion } } };
        var missingConsent = Form with { Translations = new() { ["en"] = en with { Consents = [] } } };

        Assert.Contains(missingField.Validate(Fixtures.Taxonomy), p => p.Contains("what_happened"));
        Assert.Contains(sameNotice.Validate(Fixtures.Taxonomy), p => p.Contains("notice_version"));
        Assert.Contains(missingConsent.Validate(Fixtures.Taxonomy), p => p.Contains("consent"));
    }

    [Fact]
    public void A_submission_in_english_records_the_english_notice_and_gets_english_errors()
    {
        var (report, _) = ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid() with { Language = "en" });
        Assert.Equal("en", report!.FormLanguage);
        Assert.Equal(Form.In("en").NoticeVersion, report.NoticeVersion);

        var (_, errors) = ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid(a => a.Remove("what_happened")) with { Language = "en" });
        Assert.Contains(errors, e => e.Field == "what_happened" && e.Message == ValidationMessages.English.Required);

        // An unknown language falls back to the form's own (Spanish).
        var (spanish, _) = ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid() with { Language = "xx" });
        Assert.Equal("es", spanish!.FormLanguage);
        Assert.Equal(Form.NoticeVersion, spanish.NoticeVersion);
    }

    [Fact]
    public void Support_contacts_have_english_labels()
    {
        var chile = SupportContacts.LoadDefault().For("CL")!;
        var english = chile.In("en");
        Assert.Equal(chile.Emergency.Select(c => c.Phone), english.Emergency.Select(c => c.Phone));
        Assert.All(chile.Emergency.Concat(chile.Support), c => Assert.True(c.Labels.ContainsKey("en"), c.Label));
        Assert.NotEqual(chile.ResponseTime, english.ResponseTime);
    }

    [Fact]
    public void A_valid_submission_is_accepted_and_contact_is_dropped_without_consent()
    {
        var (report, errors) = ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid(method: "email", value: "nadie@ejemplo.invalid"));
        Assert.Empty(errors);
        Assert.NotNull(report);
        Assert.StartsWith("Una app", report.FreeText);
        Assert.False(report.Answers.ContainsKey("what_happened"));
        Assert.Equal("CL", report.Country);
        Assert.Equal("metropolitana", report.Region);
        Assert.False(report.ConsentToContact);
        Assert.Null(report.ContactValue);
        Assert.Null(report.ContactMethod);
        Assert.Equal(Form.NoticeVersion, report.NoticeVersion);
    }

    [Fact]
    public void Contact_details_are_kept_only_with_consent_and_a_method()
    {
        var (ok, _) = ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid(consents: [ConsentItem.Processing, ConsentItem.Contact], method: "email", value: "nadie@ejemplo.invalid"));
        Assert.Equal("nadie@ejemplo.invalid", ok!.ContactValue);

        var (missing, errors) = ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid(consents: [ConsentItem.Processing, ConsentItem.Contact], method: "carrier_pigeon"));
        Assert.Null(missing);
        Assert.Contains(errors, e => e.Field == "contact_method");
        Assert.Contains(errors, e => e.Field == "contact_value");
    }

    [Fact]
    public void Missing_consent_and_text_are_errors()
    {
        var (report, errors) = ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid(a => a.Remove("what_happened"), consents: []));
        Assert.Null(report);
        Assert.Contains(errors, e => e.Field == "what_happened");
        Assert.Contains(errors, e => e.Field == "consent_processing");
    }

    [Fact]
    public void Unknown_fields_options_and_versions_are_refused()
    {
        Assert.Contains(ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid(a => a["password"] = ["x"])).Errors, e => e.Field == "password");
        Assert.Contains(ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid(a => a["technology"] = ["telepathy"])).Errors, e => e.Field == "technology");
        Assert.Contains(ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid(a => a["country"] = ["CL", "AR"])).Errors, e => e.Field == "country");
        Assert.Contains(ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid() with { FormVersion = "form-v0" }).Errors, e => e.Field == "form");
        Assert.Contains(ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid(a => a["what_happened"] = [new string('a', 10001)])).Errors, e => e.Field == "what_happened");
    }

    [Fact]
    public void Dont_know_is_accepted_for_technology_and_region_is_ignored_outside_chile()
    {
        var (report, errors) = ReportValidator.Validate(Form, Fixtures.Taxonomy, Valid(a =>
        {
            a["technology"] = ["dont_know"];
            a["country"] = ["AR"];
        }));
        Assert.Empty(errors);
        Assert.Null(report!.Region);
        Assert.Equal(["dont_know"], report.Answers["technology"]);
    }
}

/// <summary>FR-TAX-1: the taxonomy is versioned data with labels in three languages.</summary>
public class TaxonomyTests
{
    [Fact]
    public void Every_term_has_spanish_english_and_portuguese_labels()
    {
        foreach (var d in Fixtures.Taxonomy.Dimensions)
        {
            Assert.All(d.Terms, t => Assert.True(t.Labels.ContainsKey("es") && t.Labels.ContainsKey("en") && t.Labels.ContainsKey("pt"), $"{d.Key}:{t.Key}"));
        }

        Assert.True(Fixtures.Taxonomy.Has("technology", "neuro_bci"));
        Assert.False(Fixtures.Taxonomy.Has("technology", "telepathy"));
        Assert.False(Fixtures.Taxonomy.Dimension("actor_type")!.Multiple);
    }

    [Fact]
    public void Duplicate_terms_are_caught_by_validation()
    {
        var broken = Fixtures.Taxonomy with
        {
            Dimensions = [new TaxonomyDimension { Key = "x", Terms = [new TaxonomyTerm { Key = "a" }, new TaxonomyTerm { Key = "a" }] }],
        };
        Assert.NotEmpty(broken.Validate());
    }

    [Fact]
    public void Support_contacts_fall_back_to_chile()
    {
        var contacts = SupportContacts.LoadDefault();
        Assert.NotEmpty(contacts.For("CL")!.Emergency);
        Assert.Equal(contacts.For("CL"), contacts.For("XX"));
    }
}

/// <summary>FR-ANA-5: related reports are suggested, never merged.</summary>
public class RelatedReportsTests
{
    private static RelatedCandidate C(int id, string text, string[]? tech = null, string[]? actors = null) =>
        new(new Guid(id, 0, 0, new byte[8]), text, tech ?? [], actors ?? []);

    [Fact]
    public void Near_identical_reports_are_suggested_as_duplicates()
    {
        var a = C(1, "La aplicación de reparto Rápido Ficticio me desactivó la cuenta por una decisión automática sin explicación y perdí mis ingresos.");
        var b = C(2, "La aplicación de reparto Rápido Ficticio me desactivó la cuenta por una decisión automática, sin explicación, y perdí todos mis ingresos.");
        var c = C(3, "En el colegio instalaron cámaras con reconocimiento facial para tomar asistencia de los estudiantes.");
        var result = RelatedReports.Suggest(a, [a, b, c]);
        var only = Assert.Single(result);
        Assert.Equal(b.CaseId, only.CaseId);
        Assert.Equal(LinkKind.Duplicate, only.Kind);
    }

    [Fact]
    public void Same_actor_is_found_even_with_different_wording()
    {
        var a = C(1, "Me descartaron en una entrevista grabada.", actors: ["Selección Ficticia SpA"]);
        var b = C(2, "El sistema puso puntajes a mi cara durante el video.", actors: ["seleccion ficticia spa"]);
        Assert.Equal(LinkKind.SameActor, Assert.Single(RelatedReports.Suggest(a, [b])).Kind);
    }

    [Fact]
    public void Placeholders_alone_do_not_make_reports_similar()
    {
        var a = C(1, "[PERSON_1] [PERSON_2] [RUT_1] [EMAIL_1] [PHONE_1] cámaras");
        var b = C(2, "[PERSON_1] [PERSON_2] [RUT_1] [EMAIL_1] [PHONE_1] crédito");
        Assert.Empty(RelatedReports.Suggest(a, [b]));
    }

    [Fact]
    public void At_most_five_suggestions()
    {
        var a = C(0, "Una app de crédito me negó el préstamo con un puntaje automático.");
        var many = Enumerable.Range(1, 12).Select(i => C(i, "Una app de crédito me negó el préstamo con un puntaje automático.")).ToList();
        Assert.Equal(RelatedReports.MaxSuggestions, RelatedReports.Suggest(a, many).Count);
    }
}
