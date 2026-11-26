using System.Security.Cryptography;
using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AgentRuntime.IntegrationTests.TestSupport;

/// <summary>
/// The real API (controllers, auth, persistence, Testigo's triage worker) running in-process
/// against a throwaway Postgres container, using the Mock LLM provider.
/// Needs Docker/Podman; initialization fails when required infrastructure is unavailable.
/// A test must never be reported as passed without exercising the database.
/// Never touches a developer's own stack: its own database in a test-only container on a random
/// port.
/// </summary>
public sealed class ApiTestHost : IAsyncLifetime
{
    /// <summary>
    /// One Postgres container for the whole test process; each host gets its own database in it.
    /// Starting a container per test class was a large share of the suite's run time. Testcontainers'
    /// resource reaper removes the container when the process exits, so it's never disposed here.
    /// </summary>
    private static readonly Lazy<Task<PostgreSqlContainer>> SharedPostgres = new(StartSharedPostgresAsync);

    private static async Task<PostgreSqlContainer> StartSharedPostgresAsync()
    {
        var container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
            .WithDatabase("testigo_test")
            .WithUsername("testigo")
            .WithPassword("testigo_test_password")
            // Headroom over the default 100: hosts are disposed (and their connections released)
            // one after another.
            .WithCommand("-c", "max_connections=300")
            .Build();
        await container.StartAsync();
        return container;
    }

    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The API test host isn't initialized.");

    /// <summary>This host's own database in the shared container.</summary>
    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("The API test host isn't initialized.");

    /// <summary>The reporter app's shared key in tests (Testigo:Intake:SharedKey).</summary>
    public const string IntakeKey = "test-intake-key-0123456789abcdefghijklmnop";

    /// <summary>Extra configuration for a test class (applied over the defaults below).</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    /// <summary>Extra service registrations for a test class.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }

    public async Task InitializeAsync()
    {
        PostgreSqlContainer postgres;
        try
        {
            postgres = await SharedPostgres.Value;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Required integration-test Postgres could not start. No database-backed tests were executed.", ex);
        }

        // A fresh database per host keeps test classes as isolated as their own container did.
        var database = $"testigo_test_{Guid.NewGuid():n}";
        await using (var admin = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        _connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = database }.ConnectionString;

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = ConnectionString,
            ["Llm:Provider"] = "Mock",
            ["Auth:Mode"] = "accounts",
            ["Auth:AllowSignup"] = "true",
            ["Secrets:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Dashboard:BaseUrl"] = "http://dashboard.test",
            // Fixtures create the first account directly; SetupTokenTests covers the token.
            ["Auth:SetupTokenRequired"] = "false",
            ["Logging:LogLevel:Default"] = "Warning",
            // Testigo: throwaway keys per host, a known intake key, and cheap passphrase hashing.
            ["Testigo:Keys:MasterKeys:mk1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Testigo:Keys:LookupKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Testigo:Intake:SharedKey"] = IntakeKey,
            ["Testigo:Passphrase:MemoryKiB"] = "1024",
            ["Testigo:Passphrase:Iterations"] = "1",
            ["Testigo:Worker:PollSeconds"] = "1",
        };
        foreach (var (k, v) in Settings) settings[k] = v;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            // UseSetting, not ConfigureAppConfiguration: Program reads its configuration (the
            // connection string among it) while building, before late configuration sources apply.
            foreach (var (key, value) in settings) b.UseSetting(key, value);
            if (ConfigureServices is { } configure) b.ConfigureServices(configure);
        });

        // Never let a misapplied setting point the tests at a developer's own database: the settings
        // that reach outside the process are also set as environment variables while the host
        // starts, which WebApplication.CreateBuilder reads before anything else.
        string[] critical = ["ConnectionStrings:Postgres"];
        var previous = critical.ToDictionary(k => k, k => Environment.GetEnvironmentVariable(k.Replace(":", "__")));
        foreach (var key in critical) Environment.SetEnvironmentVariable(key.Replace(":", "__"), settings[key]);
        IConfiguration configuration;
        try
        {
            configuration = _factory.Services.GetRequiredService<IConfiguration>(); // starts the host
        }
        finally
        {
            foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key.Replace(":", "__"), value);
        }

        var configured = configuration.GetConnectionString("Postgres");
        if (configured != ConnectionString)
        {
            throw new InvalidOperationException($"The API test host isn't using its throwaway database (got '{configured}').");
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        if (_connectionString is null || !SharedPostgres.IsValueCreated) return;

        // Hand this host's connections back to the shared server. Npgsql pools are process-wide and
        // outlive the host, so without this every finished test class keeps its idle connections
        // and later classes hit "too many clients". FORCE also ends any the pool doesn't own.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_connectionString));
        var database = new NpgsqlConnectionStringBuilder(_connectionString).Database;
        try
        {
            await using var admin = new NpgsqlConnection((await SharedPostgres.Value).GetConnectionString());
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
            // Best effort: the container is thrown away when the test process exits anyway.
        }
    }

    /// <summary>A new organization (via sign-up) and a signed-in session of its owner.</summary>
    public async Task<Organization> CreateOrganizationAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityService>();
        var email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():n}@example.test";
        var signedUp = await identity.SignUpAsync(email, Password, name, name, null, CancellationToken.None);
        if (signedUp.Value?.SessionToken is not { } token) throw new InvalidOperationException(signedUp.Error ?? "No session.");
        return new Organization(signedUp.Value.TenantId, token) { OwnerUserId = signedUp.Value.User.UserId, OwnerEmail = email };
    }

    /// <summary>Another person in <paramref name="org"/> (invited with <paramref name="role"/>, then
    /// signed up), with their own session.</summary>
    public async Task<Organization> AddMemberAsync(Organization org, string name, TenantRole role)
    {
        using var scope = Factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityService>();
        var owner = new Caller(org.TenantId, TenantRole.Owner, org.OwnerUserId, org.OwnerEmail, false, null);
        var email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():n}@example.test";
        var invited = await identity.InviteAsync(owner, email, role, CancellationToken.None);
        if (invited.Error is not null) throw new InvalidOperationException(invited.Error);
        var signedUp = await identity.SignUpAsync(email, Password, name, null, invited.Value.Token, CancellationToken.None);
        if (signedUp.Value?.SessionToken is not { } token) throw new InvalidOperationException(signedUp.Error ?? "No session.");
        return new Organization(org.TenantId, token) { OwnerUserId = signedUp.Value.User.UserId, OwnerEmail = email };
    }

    /// <summary>A client signed in as the organization's person, with their session cookie.</summary>
    public HttpClient ClientFor(Organization org)
    {
        var client = Factory.CreateClient();
        var cookie = Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>().Value.CookieName;
        client.DefaultRequestHeaders.Add("Cookie", $"{cookie}={org.SessionToken}");
        return client;
    }

    private const string Password = "correct-horse-battery-staple-1";
}

public sealed record Organization(string TenantId, string SessionToken)
{
    /// <summary>The person the session belongs to.</summary>
    public string? OwnerUserId { get; init; }
    public string? OwnerEmail { get; init; }
}
