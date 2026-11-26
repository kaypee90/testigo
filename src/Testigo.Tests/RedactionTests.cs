using System.Text.RegularExpressions;
using Testigo.Redaction;

namespace Testigo.Tests;

/// <summary>§10.4: the RUT check digit, and synthetic RUTs that always fail it.</summary>
public class RutTests
{
    [Theory]
    [InlineData(12345678L, '5')]
    [InlineData(11111111L, '1')]
    [InlineData(6L, 'K')]
    [InlineData(31L, '0')]
    public void Check_digit_is_modulo_11(long body, char expected) => Assert.Equal(expected, Rut.CheckDigit(body));

    [Fact]
    public void A_rut_built_with_its_check_digit_is_valid_in_any_format()
    {
        var dv = Rut.CheckDigit(12345678);
        Assert.True(Rut.IsValid($"12.345.678-{dv}"));
        Assert.True(Rut.IsValid($"12345678{dv}"));
        Assert.False(Rut.IsValid("12.345.678-9"));
        Assert.False(Rut.IsValid("9.876.543-1"));
        Assert.False(Rut.IsValid("abc"));
        Assert.False(Rut.IsValid(null));
    }

    /// <summary>Every RUT-shaped value in test data, seeds and evals must fail the check digit (§10.4).</summary>
    [Fact]
    public void No_synthetic_data_file_contains_a_valid_rut()
    {
        var root = RepoRoot();
        var shaped = new Regex(@"\b\d{1,2}\.\d{3}\.\d{3}\s?-\s?[\dkK]\b|\b\d{7,8}-[\dkK]\b");
        var places = new[] { "src/Testigo", "src/Testigo.Tests", "src/Testigo.Infrastructure", "src/Testigo.Reporter", "evals/testigo" }
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists);
        var offenders = new List<string>();
        foreach (var file in places.SelectMany(p => Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (Path.GetExtension(file) is not (".cs" or ".json" or ".jsonl" or ".txt" or ".md" or ".cshtml" or ".resx" or ".sql")) continue;
            foreach (Match m in shaped.Matches(File.ReadAllText(file)))
            {
                if (Rut.IsValid(m.Value)) offenders.Add($"{Path.GetRelativePath(root, file)}: {m.Value}");
            }
        }

        Assert.True(offenders.Count == 0, "Valid RUTs found: " + string.Join(", ", offenders));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Testigo.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>FR-ANA-3: rule-based redaction with typed placeholders, done by code.</summary>
public class RedactorTests
{
    private readonly Redactor redactor = new();

    [Fact]
    public void Common_identifiers_are_replaced_with_typed_placeholders()
    {
        const string text = "Me llamo Camila Rojas y mi RUT es 12.345.678-9. Escríbanme a camila.rojas@ejemplo.invalid o al +56 9 8765 4321.";
        var r = redactor.Redact(text);
        Assert.DoesNotContain("Camila", r.Text);
        Assert.DoesNotContain("12.345.678", r.Text);
        Assert.DoesNotContain("@ejemplo", r.Text);
        Assert.DoesNotContain("8765", r.Text);
        Assert.Contains("[PERSON_1]", r.Text);
        Assert.Contains("[RUT_1]", r.Text);
        Assert.Contains("[EMAIL_1]", r.Text);
        Assert.Contains("[PHONE_1]", r.Text);
        Assert.Equal(Redactor.RulesVersion, r.MethodVersion);
        Assert.Equal(text, r.Restore(r.Text));
    }

    [Fact]
    public void Ruts_are_redacted_even_with_a_wrong_check_digit_and_without_one()
    {
        var r = redactor.Redact("Mi RUT 9.876.543-1 y el de mi hermano, RUN: 23456789.");
        Assert.Equal(2, r.Spans.Count(s => s.Type == PersonalDataType.Rut));
        Assert.DoesNotContain("9.876.543", r.Text);
        Assert.DoesNotContain("23456789", r.Text);
    }

    [Fact]
    public void The_same_value_gets_the_same_placeholder()
    {
        var r = redactor.Redact("Josefa llamó al 22 345 6789. Después Josefa volvió a llamar al 223456789.");
        Assert.Equal(2, Regex.Matches(r.Text, Regex.Escape("[PERSON_1]")).Count);
        Assert.Equal(2, Regex.Matches(r.Text, Regex.Escape("[PHONE_1]")).Count);
        Assert.DoesNotContain("[PHONE_2]", r.Text);
    }

    [Fact]
    public void Amounts_dates_and_years_are_kept()
    {
        const string text = "Me cobraron $ 1.500.000 y luego 2.300.000 pesos entre 2024 y 2025, el 12/03/2025.";
        var r = redactor.Redact(text);
        Assert.Equal(text, r.Text);
        Assert.Empty(r.Spans);
    }

    [Fact]
    public void Addresses_urls_handles_and_coordinates()
    {
        var r = redactor.Redact("Vivo en calle Los Aromos 1234, depto 56. El video está en https://ejemplo.invalid/v/123. Me escribió @usuario_falso desde -33.4489, -70.6693.");
        Assert.Contains(PersonalDataType.Address, r.Types);
        Assert.Contains(PersonalDataType.Url, r.Types);
        Assert.Contains(PersonalDataType.Handle, r.Types);
        Assert.Contains(PersonalDataType.Location, r.Types);
        Assert.Contains("[URL_1].", r.Text); // trailing period kept outside the URL
    }

    [Fact]
    public void Names_after_a_trigger_are_redacted_even_when_not_in_the_list()
    {
        var r = redactor.Redact("Mi hija se llama Xiomara Quintanilla y estudia en un liceo.");
        Assert.DoesNotContain("Xiomara", r.Text);
        Assert.Contains("[PERSON_1]", r.Text);
    }

    [Fact]
    public void Extra_values_from_the_model_step_are_applied_by_code()
    {
        const string text = "El supervisor, el tío Lucho, revisaba las grabaciones. Lucho nos dijo que era normal.";
        var r = redactor.Redact(text, [new ExtraPersonalData("Lucho", PersonalDataType.Person), new ExtraPersonalData("x", PersonalDataType.Person), new ExtraPersonalData("algo", "SECRET")]);
        Assert.DoesNotContain("Lucho", r.Text);
        Assert.Equal(2, Regex.Matches(r.Text, Regex.Escape("[PERSON_1]")).Count);
        Assert.Equal(Redactor.RulesVersion + "+model", r.MethodVersion);
    }

    [Fact]
    public void Redacted_ranges_map_back_to_the_original()
    {
        const string text = "Hola, soy la Valentina Soto y trabajo en una bodega con cámaras.";
        var r = redactor.Redact(text);
        var at = r.Text.IndexOf("trabajo en una bodega", StringComparison.Ordinal);
        var (start, length) = r.ToOriginal(at, "trabajo en una bodega".Length);
        Assert.Equal("trabajo en una bodega", text.Substring(start, length));

        var p = r.Text.IndexOf("[PERSON_1]", StringComparison.Ordinal);
        var (ps, pl) = r.ToOriginal(p, "[PERSON_1] y".Length);
        Assert.Equal("Valentina Soto y", text.Substring(ps, pl));
    }

    [Fact]
    public void Detect_reports_the_kinds_found_for_the_reporter_warning()
    {
        Assert.Empty(redactor.Detect("La aplicación de la empresa me negó el crédito sin explicar por qué."));
        Assert.Equal(new[] { PersonalDataType.Email }, redactor.Detect("Mi correo es nadie@ejemplo.invalid"));
    }

    [Fact]
    public void Long_input_finishes_quickly()
    {
        var text = string.Concat(Enumerable.Repeat("Texto de relleno con números 123 456 y palabras Sr. ", 400));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _ = redactor.Redact(text);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }
}
