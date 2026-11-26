using System.Text.Json;
using AgentRuntime.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Testigo.Infrastructure;
using Testigo.Infrastructure.Demo;
using Testigo.Infrastructure.Triage;
using Testigo.Redaction;

namespace Testigo.Tests;

/// <summary>NFR-SEC-4: master keys come from configuration or a key file outside the database.</summary>
public class KeyLoadingTests
{
    [Fact]
    public void A_missing_key_file_is_created_only_when_allowed_and_then_reused()
    {
        var dir = Path.Combine(Path.GetTempPath(), "testigo-keys-" + Guid.NewGuid().ToString("n"));
        var file = Path.Combine(dir, "keys.json");
        try
        {
            Assert.Throws<InvalidOperationException>(() => TestigoKeys.Load(new KeyOptions { KeyFile = file }, NullLogger.Instance));
            var first = TestigoKeys.Load(new KeyOptions { KeyFile = file, GenerateIfMissing = true }, NullLogger.Instance);
            var again = TestigoKeys.Load(new KeyOptions { KeyFile = file }, NullLogger.Instance);
            Assert.Equal(first.LookupKey, again.LookupKey);
            Assert.Equal(first.MasterKeys.ActiveKeyId, again.MasterKeys.ActiveKeyId);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Without_any_key_source_startup_fails() =>
        Assert.Throws<InvalidOperationException>(() => TestigoKeys.Load(new KeyOptions(), NullLogger.Instance));

    [Fact]
    public void Intake_is_closed_without_a_shared_key() =>
        Assert.Null(TestigoKeys.LoadIntakeKey(new IntakeOptions(), NullLogger.Instance));
}

/// <summary>The demo's Mock triage: deterministic, and every quote it gives is grounded.</summary>
public class MockTriageTests
{
    private static IEnumerable<string> EvalTexts()
    {
        var file = Path.Combine(RutTests.RepoRoot(), "evals", "testigo", "reports.jsonl");
        foreach (var line in File.ReadLines(file).Where(l => l.Trim().Length > 0))
        {
            yield return JsonDocument.Parse(line).RootElement.GetProperty("text").GetString()!;
        }
    }

    [Fact]
    public async Task Every_mock_quote_on_the_eval_set_is_grounded_and_repeatable()
    {
        var service = Fixtures.Triage(new HeuristicMockLlmProvider([new MockTriageBehavior()]));
        var texts = EvalTexts().ToList();
        Assert.Equal(30, texts.Count);
        foreach (var text in texts)
        {
            var a = await service.AnalyzeAsync("org", text, default);
            var b = await service.AnalyzeAsync("org", text, default);
            Assert.True(a.Complete, a.IncompleteReason);
            Assert.True(a.QuotesReturned == a.QuotesGrounded, $"{a.QuotesGrounded}/{a.QuotesReturned} grounded: {a.RawArguments} || {a.Redaction.Text}");
            Assert.NotEmpty(a.Terms);
            Assert.Equal(a.Terms, b.Terms);
            Assert.Equal(a.Redaction.Text, b.Redaction.Text);
        }
    }

    [Fact]
    public void The_demo_reports_are_valid_submissions()
    {
        var form = Forms.FormDefinition.LoadDefault(Fixtures.Taxonomy);
        foreach (var r in DemoSeeder.Load())
        {
            var (report, errors) = Forms.ReportValidator.Validate(form, Fixtures.Taxonomy, new Forms.ReportSubmission { FormVersion = form.Version, Answers = r.Answers, Consents = r.Consents });
            Assert.True(report is not null, string.Join(", ", errors.Select(e => $"{e.Field}: {e.Message}")));
        }
    }

    [Fact]
    public void The_mock_lists_nicknames_the_rules_miss()
    {
        var args = JsonSerializer.Serialize(MockTriageBehavior.Analyze("El supervisor, el tío Lucho, revisaba los mapas."));
        Assert.Contains("\"Lucho\"", args);
        Assert.Contains(PersonalDataType.Person, args);
    }
}

public sealed class ModelLabelTests
{
    [Theory]
    [InlineData(null, "claude-sonnet-5", "Mock")]
    [InlineData("Mock", "claude-sonnet-5", "Mock")]
    [InlineData("Ollama", "qwen3:4b-instruct", "Ollama:qwen3:4b-instruct")]
    public void The_demo_model_is_never_labelled_as_a_real_one(string? provider, string model, string expected)
    {
        var configuration = Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(new Microsoft.Extensions.Configuration.ConfigurationBuilder(),
                new Dictionary<string, string?> { ["Llm:Provider"] = provider, ["Llm:Model"] = model })
            .Build();
        Assert.Equal(expected, TestigoServiceCollectionExtensions.DefaultModelLabel(configuration));
    }
}
