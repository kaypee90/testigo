using System.Security.Cryptography;
using System.Text;
using AgentRuntime.Infrastructure.Llm;
using AgentRuntime.LLM;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testigo.Forms;
using Testigo.Infrastructure.Demo;
using Testigo.Infrastructure.Persistence;
using Testigo.Infrastructure.Services;
using Testigo.Infrastructure.Triage;
using Testigo.Security;
using Testigo.Support;
using Testigo.Taxonomy;
using Testigo.Triage;

namespace Testigo.Infrastructure;

public static class TestigoServiceCollectionExtensions
{
    /// <summary>Testigo's case management (D1: a native module).</summary>
    public static IServiceCollection AddTestigo(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TestigoOptions>(configuration.GetSection(TestigoOptions.Section));
        var connectionString = configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        services.AddDbContextFactory<TestigoDbContext>(o => o.UseNpgsql(connectionString, x => x.MigrationsHistoryTable("__ef_migrations", TestigoDbContext.Schema)));
        services.TryAddSingleton(TimeProvider.System);

        // The data files are versioned and replaceable (VALIDATE items are data, not code).
        var data = configuration.GetSection("Testigo:Data");
        services.AddSingleton(_ => HarmTaxonomy.LoadDefault(data["Taxonomy"]));
        services.AddSingleton(sp => FormDefinition.LoadDefault(sp.GetRequiredService<HarmTaxonomy>(), data["Form"]));
        services.AddSingleton(_ => SupportContacts.LoadDefault(data["SupportContacts"]));
        services.AddSingleton(_ => new DangerDetector(DangerRuleSet.LoadDefault(data["DangerRules"])));

        services.AddSingleton(sp => TestigoKeys.Load(sp.GetRequiredService<IOptions<TestigoOptions>>().Value.Keys, sp.GetRequiredService<ILoggerFactory>().CreateLogger("Testigo.Keys")));
        services.AddSingleton(sp => sp.GetRequiredService<TestigoKeys>().MasterKeys);
        services.AddSingleton<IDataKeyStore, EfDataKeyStore>();
        services.AddSingleton<IFieldCipher, EnvelopeFieldCipher>();
        services.AddSingleton(_ => configuration.GetSection(PassphraseHashOptions.Section).Get<PassphraseHashOptions>() ?? new PassphraseHashOptions());
        services.AddSingleton(sp => new PassphraseHasher(sp.GetRequiredService<PassphraseHashOptions>()));
        services.AddSingleton<IntakeGate>();

        services.AddSingleton(_ =>
        {
            var o = configuration.GetSection(TriageOptions.Section).Get<TriageOptions>() ?? new TriageOptions();
            // The model actually used is recorded with every suggestion (FR-ANA-7).
            return configuration.GetSection(TriageOptions.Section)["ModelLabel"] is null ? o with { ModelLabel = DefaultModelLabel(configuration) } : o;
        });
        services.AddSingleton<ITriageCache, EfTriageCache>();
        services.AddSingleton(sp => new TriageService(sp.GetRequiredService<ILLMProvider>(), sp.GetRequiredService<HarmTaxonomy>(), sp.GetRequiredService<DangerDetector>(),
            sp.GetRequiredService<ITriageCache>(), sp.GetRequiredService<TriageOptions>()));
        services.AddSingleton<IMockLlmBehavior, MockTriageBehavior>();

        services.AddSingleton<CaseAudit>();
        services.AddSingleton<IntakeService>();
        services.AddSingleton<TriageProcessor>();
        services.AddSingleton<CaseService>();
        services.AddSingleton<StaffAccess>();
        services.AddSingleton<DemoSeeder>();
        services.AddHostedService<TriageWorker>();
        return services;
    }

    /// <summary>Applies Testigo's migrations, then the demo setup when configured. Call after the platform's own migrations.</summary>
    /// <summary>
    /// "Provider:model" as recorded with every suggestion; the scripted demo model is just "Mock",
    /// so a configured model name never makes it look like a real one ran.
    /// </summary>
    internal static string DefaultModelLabel(IConfiguration configuration)
    {
        var provider = configuration["Llm:Provider"];
        return string.IsNullOrWhiteSpace(provider) || provider.Equals("Mock", StringComparison.OrdinalIgnoreCase)
            ? "Mock"
            : $"{provider}:{configuration["Llm:Model"] ?? "default"}";
    }

    public static async Task MigrateTestigoAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        var factory = host.Services.GetRequiredService<IDbContextFactory<TestigoDbContext>>();
        await using (var db = await factory.CreateDbContextAsync(cancellationToken))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        // Load the keys now, so a missing key file stops startup instead of the first report.
        _ = host.Services.GetRequiredService<TestigoKeys>();
        _ = host.Services.GetRequiredService<IntakeGate>();
        await host.Services.GetRequiredService<DemoSeeder>().RunAsync(cancellationToken);
    }
}

/// <summary>
/// The shared key that only the reporter app holds (NFR-SEC-7): intake endpoints refuse every
/// other caller. Compared in constant time. With no key configured, intake is closed.
/// </summary>
public sealed class IntakeGate
{
    private readonly byte[]? expected;

    public IntakeGate(IOptions<TestigoOptions> options, ILoggerFactory loggers)
    {
        var key = TestigoKeys.LoadIntakeKey(options.Value.Intake, loggers.CreateLogger("Testigo.Intake"));
        expected = key is null ? null : SHA256.HashData(Encoding.UTF8.GetBytes(key));
        if (key is null) loggers.CreateLogger("Testigo.Intake").LogWarning("No intake shared key is configured: the public report form is closed.");
    }

    public const string HeaderName = "X-Testigo-Intake-Key";
    public bool Open => expected is not null;

    public bool Allows(string? presented) =>
        expected is not null && presented is not null && CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(presented)));
}
