using AgentRuntime.Infrastructure.Llm;
using AgentRuntime.Infrastructure.Persistence;
using AgentRuntime.Infrastructure.Secrets;
using AgentRuntime.LLM;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AgentRuntime.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Accounts, organizations, sessions and 2FA; the audit log; the database; the model.</summary>
    public static IServiceCollection AddAgentRuntimeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

        // A factory (safe for singletons) plus a scoped context from that same factory (controllers).
        services.AddDbContextFactory<AgentDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<AgentDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AgentDbContext>>().CreateDbContext());

        services.Configure<SecretsOptions>(configuration.GetSection(SecretsOptions.SectionName));
        services.AddSingleton<SecretProtector>();
        services.AddSingleton<AgentRuntime.Safety.IAuditLog, PostgresAuditLog>();

        // Accounts and access (docs/platform.md).
        services.Configure<Identity.DashboardOptions>(configuration.GetSection(Identity.DashboardOptions.SectionName));
        services.Configure<Identity.AuthOptions>(configuration.GetSection(Identity.AuthOptions.SectionName));
        services.AddSingleton<Identity.IdentityService>();
        services.AddSingleton<Identity.TwoFactorService>();
        services.Configure<Identity.PlatformEmailOptions>(configuration.GetSection(Identity.PlatformEmailOptions.SectionName));
        services.AddSingleton<Identity.PlatformMailer>();
        services.AddSingleton<Identity.PlatformAdminService>();
        services.TryAddSingleton(TimeProvider.System);

        AddLlmProvider(services, configuration);
        return services;
    }

    /// <summary>The one model provider named by Llm:Provider (Mock when unset), with its HTTP client.</summary>
    public static void AddLlmProvider(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        var providerName = LlmOptions.CanonicalProvider(configuration[$"{LlmOptions.SectionName}:Provider"]);
        // appsettings.json ships "BaseUrl": "" as a placeholder: blank means unset.
        var configuredBaseUrl = LlmOptions.NormalizeBaseUrl(providerName, configuration[$"{LlmOptions.SectionName}:BaseUrl"]);
        var timeout = TimeSpan.FromSeconds(Math.Max(30, configuration.GetValue($"{LlmOptions.SectionName}:TimeoutSeconds", 300)));

        Uri BaseAddress(string fallback) => new((configuredBaseUrl ?? fallback).TrimEnd('/') + "/");

        switch (providerName)
        {
            case "Anthropic":
                services.AddHttpClient<AnthropicProvider>(c => { c.BaseAddress = BaseAddress("https://api.anthropic.com"); c.Timeout = timeout; })
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = !configuration.GetValue<bool>("Testigo:LocalModelsOnly") });
                services.AddSingleton<ILLMProvider>(sp => sp.GetRequiredService<AnthropicProvider>());
                break;
            case "OpenAI":
                services.AddHttpClient<OpenAIProvider>(c => { c.BaseAddress = BaseAddress("https://api.openai.com"); c.Timeout = timeout; })
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = !configuration.GetValue<bool>("Testigo:LocalModelsOnly") });
                services.AddSingleton<ILLMProvider>(sp => sp.GetRequiredService<OpenAIProvider>());
                break;
            case "Gemini":
                services.AddHttpClient<GeminiProvider>(c => { c.BaseAddress = BaseAddress("https://generativelanguage.googleapis.com"); c.Timeout = timeout; })
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = !configuration.GetValue<bool>("Testigo:LocalModelsOnly") });
                services.AddSingleton<ILLMProvider>(sp => sp.GetRequiredService<GeminiProvider>());
                break;
            case "Ollama":
                services.AddHttpClient<OllamaProvider>(c =>
                {
                    var inContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
                    c.BaseAddress = BaseAddress(inContainer ? "http://host.docker.internal:11434" : "http://localhost:11434");
                    c.Timeout = timeout;
                }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = !configuration.GetValue<bool>("Testigo:LocalModelsOnly"),
                });
                services.AddSingleton<ILLMProvider>(sp => sp.GetRequiredService<OllamaProvider>());
                break;
            default:
                services.AddSingleton<HeuristicMockLlmProvider>();
                services.AddSingleton<ILLMProvider>(sp => sp.GetRequiredService<HeuristicMockLlmProvider>());
                break;
        }
    }

    /// <summary>Applies pending EF Core migrations and creates the configured local admin at
    /// startup, so `docker compose up` works with no manual step.</summary>
    public static async Task MigrateDatabaseAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        await db.Database.MigrateAsync();

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var auth = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Identity.AuthOptions>>().Value;
        if (Identity.LocalAdminGuard.Problem(auth, configuration.GetValue<bool>("ForwardedHeaders:Enabled") || auth.AlwaysSecureCookie) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        var identity = host.Services.GetRequiredService<Identity.IdentityService>();
        await identity.EnsureLocalAdminAsync();
        await identity.AnnounceSetupAsync();
    }
}
