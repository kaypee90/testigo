using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AgentRuntime.Api.Platform;
using AgentRuntime.Infrastructure;
using Testigo.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Every JSON response uses snake_case properties and enum names as strings.
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info.Title = "Testigo API";
    doc.Info.Description = "The staff API (session cookie) and the intake API (reporter app only). See docs/platform.md.";
    return Task.CompletedTask;
}));

// Every request is authenticated by its session cookie and scoped to one organization
// (docs/platform.md). Testigo's intake opts out explicitly: the reporter app holds a shared key.
builder.Services.AddHttpContextAccessor();
// Which build is running, for Platform admin's overview.
builder.Services.AddSingleton<BuildInfo>();
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization(Policies.Add);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Password and code guessing: a few attempts per minute per client address.
    var signInAttempts = Math.Max(1, builder.Configuration.GetValue("Auth:SignInAttemptsPerMinute", 10));
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = signInAttempts, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Report submissions reach the API only through the reporter app, which limits each client
    // itself with short-lived in-memory buckets. Here one global bucket caps the whole intake, with
    // no address involved (NFR-SEC-3, NFR-SEC-9).
    var intakePerMinute = Math.Max(1, builder.Configuration.GetValue("Testigo:Intake:SubmissionsPerMinute", 30));
    o.AddPolicy("intake", _ => RateLimitPartition.GetFixedWindowLimiter("intake",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = intakePerMinute * 4, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddAgentRuntimeInfrastructure(builder.Configuration);
// Testigo's case management (D1: a native module).
builder.Services.AddTestigo(builder.Configuration);
builder.Services.AddSingleton<AccountDeletionService>();

// A hiccup in a background service (the triage worker losing its database connection) logs and
// keeps going rather than stopping the API.
builder.Services.Configure<Microsoft.Extensions.Hosting.HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = Microsoft.Extensions.Hosting.BackgroundServiceExceptionBehavior.Ignore);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // The dashboard signs in with a cookie, so its origin may send credentials.
        .AllowCredentials());
});

// Traces, logs and metrics over OpenTelemetry when an exporter is configured (docs/telemetry.md).
// There is no error-reporting service: exceptions could carry report content (NFR-OPS-4).
builder.AddTestigoTelemetry();

var app = builder.Build();

// Behind Caddy: the original scheme and client address, before anything reads them.
app.UseTestigoProxyHeaders(app.Configuration);

await app.MigrateDatabaseAsync();
await app.MigrateTestigoAsync();

// The API description is public, so contributors and integrators can build against it.
app.MapOpenApi().AllowAnonymous();

app.UseRouting();

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>Visible to the integration tests' WebApplicationFactory.</summary>
public partial class Program;
