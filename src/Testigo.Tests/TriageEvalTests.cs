using Testigo.Infrastructure.Evals;
using Testigo.Infrastructure.Triage;
using Testigo.Redaction;
using AgentRuntime.Infrastructure.Llm;

namespace Testigo.Tests;

/// <summary>The eval scorer (requirements §10.2) and the Phase 0 numbers on the synthetic set (§12).</summary>
public sealed class TriageEvalTests
{
    [Theory]
    [InlineData("Mi nombre es [PERSONA_1] y vivo en Santiago.", "Camila Rojas", false)]
    [InlineData("Habló con Rojas en la oficina.", "Camila Rojas", true)]
    [InlineData("RUT [RUT_1], y el 12345678-9 también.", "12.345.678-9", true)]
    [InlineData("Mi hermano usa el dispositivo. Contacto: [USUARIO_1]", "@hermano_ficticio", false)]
    [InlineData("Contacto: @Hermano_Ficticio", "@hermano_ficticio", true)]
    [InlineData("Escribe a [CORREO_1].", "benjamin.soto@ejemplo.invalid", false)]
    [InlineData("Llamó Jose Muñoz.", "José Muñoz", true)]
    public void Leaks_are_found_without_counting_ordinary_words(string redacted, string identifier, bool leaked) =>
        Assert.Equal(leaked, TriageEval.Leaked(redacted, identifier));

    [Fact]
    public async Task The_mock_meets_the_phase_0_targets_on_the_eval_set()
    {
        var reports = TriageEval.Load(Path.Combine(RutTests.RepoRoot(), "evals", "testigo", "reports.jsonl"));
        Assert.Equal(30, reports.Count);
        var result = await TriageEval.RunAsync(reports, new HeuristicMockLlmProvider([new MockTriageBehavior()]), llm => Fixtures.Triage(llm), default);

        Assert.Equal(1.0, result.CitationValidity);
        Assert.Equal(1.0, result.EvidenceValidity);
        Assert.Empty(result.Leaks);
        Assert.Equal(1.0, result.Danger.Recall);
        Assert.Equal(result.Reports, result.Repeatable);
        Assert.Equal(3, result.Injections.Count);
        Assert.All(result.Injections, i => Assert.True(i.Passed, i.ToString()));
        Assert.Contains("Citation validity", TriageEval.Format(result));
        Assert.Contains("Redaction leakage", TriageEval.Format(result));
    }

    [Theory]
    [InlineData("Mi pasaporte es AB1234567.", "AB1234567")]
    [InlineData("Cédula n° 12.345.678, emitida ayer.", "12.345.678")]
    [InlineData("DNI: 30111222", "30111222")]
    public void Labelled_document_numbers_are_redacted(string text, string number)
    {
        var result = new Redactor().Redact(text);
        Assert.DoesNotContain(number, result.Text);
        Assert.Contains(PersonalDataType.IdNumber, result.Types);
    }

    [Fact]
    public void A_document_word_without_a_number_is_kept() =>
        Assert.Equal("Mi pasaporte vencido no sirve.", new Redactor().Redact("Mi pasaporte vencido no sirve.").Text);
}
