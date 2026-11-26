using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Testigo.Reporter;

var builder = WebApplication.CreateBuilder(args);

// No request logging, no address in any log line (NFR-SEC-3): the framework's own logs stay at
// Warning, and nothing here logs a request.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

builder.Services.Configure<ReporterOptions>(builder.Configuration.GetSection(ReporterOptions.Section));
builder.Services.AddSingleton(new TextCatalog(builder.Configuration.GetSection(ReporterOptions.Section).Get<ReporterOptions>()?.Languages));
builder.Services.AddHttpContextAccessor();
// Each request's texts, in the language its address names (/en/…), Spanish otherwise.
builder.Services.AddScoped(sp => sp.GetRequiredService<TextCatalog>().For(LanguageMiddleware.LanguageOf(sp.GetRequiredService<IHttpContextAccessor>().HttpContext)));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ClientBuckets>();
builder.Services.AddSingleton<Testigo.Redaction.Redactor>();
builder.Services.AddMemoryCache();
builder.Services.AddTransient<IntakeKeyHandler>();
builder.Services.AddHttpClient<IntakeClient>((sp, http) =>
    {
        var o = sp.GetRequiredService<IOptions<ReporterOptions>>().Value;
        http.BaseAddress = new Uri(o.ApiBaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("testigo-reporter");
    })
    .AddHttpMessageHandler<IntakeKeyHandler>();

// Razor Pages validate the antiforgery token on every POST by default.
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "testigo_af";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Cookie.HttpOnly = true;
    // One cookie for every language: by default it would be scoped to the path base (/en).
    o.Cookie.Path = "/";
});
builder.Services.AddSingleton<SubmissionLimiter>();

var app = builder.Build();
var reporter = app.Services.GetRequiredService<IOptions<ReporterOptions>>().Value;

if (reporter.BehindProxy)
{
    // Only for the rate-limit bucket; the address is never stored or logged.
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    foreach (var network in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" }) forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    app.UseForwardedHeaders(forwarded);
}

// First, so every later step (error pages included) sees the request's language.
app.UseMiddleware<LanguageMiddleware>();
// Never the developer exception page: it could show what a reporter typed.
app.UseExceptionHandler("/Error");
// Empty error responses (404, a failed antiforgery check) get a page with the banner too.
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();

/// <summary>Visible to the tests' WebApplicationFactory.</summary>
public partial class Program;
