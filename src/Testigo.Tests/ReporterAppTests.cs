using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testigo.Reporter;

namespace Testigo.Tests;

/// <summary>
/// The public reporter app against a stub of the API's intake endpoints: the form, the
/// personal-data warning, the acknowledgement, and the security properties of every page
/// (FR-INT-1, FR-INT-2, FR-INT-3, FR-INT-5, FR-INT-9, NFR-SEC-2, NFR-SEC-3, NFR-SEC-6, NFR-SEC-7, §12 banner).
/// </summary>
public sealed class ReporterAppTests
{
    private const string Banner = "Prototipo con datos ficticios. No es un canal oficial de denuncia. No envíe información real.";
    private const string Csp = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
    private const string Story = "La aplicación de préstamos rechazó mi solicitud sin explicación y no pude hablar con ninguna persona.";

    [Theory]
    [InlineData("/")]
    [InlineData("/reportar")]
    [InlineData("/Error")]
    [InlineData("/no-existe")]
    [InlineData("/en")]
    [InlineData("/en/reportar")]
    [InlineData("/en/no-existe")]
    public async Task Every_page_has_the_banner_and_the_strict_headers(string path)
    {
        using var app = new ReporterApp();
        var response = await app.Client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(Banner, WebUtility.HtmlDecode(html));
        Assert.Contains("data-testid=\"prototype-banner\"", html);
        Assert.Equal(Csp, Header(response, "Content-Security-Policy"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
        Assert.Contains("noindex", Header(response, "X-Robots-Tag"));
        AssertNoThirdPartyOrInlineCode(html);
    }

    [Fact]
    public async Task Spanish_pages_offer_english_at_the_same_address_under_en()
    {
        using var app = new ReporterApp();
        var html = await app.Client.GetStringAsync("/reportar");

        Assert.Contains("<html lang=\"es\">", html);
        Assert.Contains("data-testid=\"language-switch\"", html);
        Assert.Contains("<a href=\"/en/reportar\" hreflang=\"en\" lang=\"en\">English</a>", html);
        Assert.Contains("action=\"/reportar\"", html);
        Assert.DoesNotContain(new Texts("en")["banner"], WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task English_pages_keep_the_spanish_banner_and_are_in_english()
    {
        using var app = new ReporterApp();
        var start = WebUtility.HtmlDecode(await app.Client.GetStringAsync("/en"));
        var form = WebUtility.HtmlDecode(await app.Client.GetStringAsync("/en/reportar"));
        var english = new Texts("en");

        foreach (var html in new[] { start, form })
        {
            Assert.Contains("<html lang=\"en\">", html);
            Assert.Contains(Banner, html);
            Assert.Contains(english["banner"], html);
            Assert.Contains("hreflang=\"es\" lang=\"es\">Español</a>", html);
        }

        Assert.Contains(english["intro.heading"], start);
        Assert.Contains("href=\"/en/reportar\"", start);
        Assert.Contains("<a href=\"/\" hreflang=\"es\"", start);
        Assert.Contains(english["form.submit"], form);
        Assert.Contains("What happened?", form);
        Assert.Contains("action=\"/en/reportar\"", form);
        Assert.Contains("<a href=\"/reportar\" hreflang=\"es\"", form);
        Assert.Contains("?lang=en", app.Api.FormQueries);
    }

    [Fact]
    public async Task A_report_sent_in_english_says_so_and_shows_the_receipt_in_english()
    {
        using var app = new ReporterApp();
        var form = await app.FormFieldsAsync("/en/reportar");
        form["a.what_happened"] = "The loan app turned down my application with no explanation.";

        var response = await app.Client.PostAsync("/en/reportar", new FormUrlEncodedContent(form));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(new Texts("en")["done.heading"], html);
        Assert.Contains(Banner, html);
        // No switch after sending: following it would lose the receipt.
        Assert.DoesNotContain("data-testid=\"language-switch\"", html);
        using var body = System.Text.Json.JsonDocument.Parse(Assert.Single(app.Api.Posts).Body);
        Assert.Equal("en", body.RootElement.GetProperty("language").GetString());
    }

    [Fact]
    public async Task The_personal_data_warning_is_in_the_page_language()
    {
        using var app = new ReporterApp();
        var form = await app.FormFieldsAsync("/en/reportar");
        form["a.what_happened"] = "Write to me at persona.ficticia@example.com";

        var html = WebUtility.HtmlDecode(await (await app.Client.PostAsync("/en/reportar", new FormUrlEncodedContent(form))).Content.ReadAsStringAsync());

        Assert.Contains(new Texts("en")["pii.heading"], html);
        Assert.Contains(Testigo.Redaction.PersonalDataType.English(Testigo.Redaction.PersonalDataType.Email), html);
        Assert.Empty(app.Api.Posts);
    }

    [Fact]
    public async Task Only_the_configured_languages_are_offered()
    {
        using var app = new ReporterApp(settings: new() { ["Reporter:Languages:0"] = "es" });
        var html = await app.Client.GetStringAsync("/");

        Assert.DoesNotContain("data-testid=\"language-switch\"", html);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/en/reportar")).StatusCode);
    }

    [Fact]
    public void A_language_without_a_texts_file_is_refused_at_start_up()
    {
        Assert.Throws<InvalidOperationException>(() => new TextCatalog(["es", "xx"]));
        Assert.Equal(["es", "en"], new TextCatalog(["en"]).Languages);
        Assert.Equal("/en", TextCatalog.PathIn("en", "/"));
        Assert.Equal("/en/reportar", TextCatalog.PathIn("en", "/reportar"));
        Assert.Equal("/reportar", TextCatalog.PathIn("es", "/reportar"));
    }

    [Fact]
    public async Task Unknown_pages_are_404_and_errors_are_500()
    {
        using var app = new ReporterApp();
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/no-existe")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await app.Client.GetAsync("/Error")).StatusCode);
    }

    [Fact]
    public async Task The_start_page_explains_the_report_and_says_the_model_is_a_demo()
    {
        using var app = new ReporterApp();
        var html = WebUtility.HtmlDecode(await app.Client.GetStringAsync("/"));
        Assert.Contains("Fono de prueba", html);
        Assert.Contains("href=\"/reportar\"", html);
        Assert.Contains(new Texts("es")["intro.ai.mock"], html);
    }

    /// <summary>FR-INT-2, NFR-AI-2: a cloud configuration never inherits a local-processing promise.</summary>
    [Theory]
    [InlineData("/", "es")]
    [InlineData("/en", "en")]
    public async Task Cloud_notice_identifies_the_provider_and_endpoint(string path, string language)
    {
        using var app = new ReporterApp(api => api.AiMode = "cloud");
        var html = WebUtility.HtmlDecode(await app.Client.GetStringAsync(path));
        Assert.Contains(new Texts(language)["intro.ai.cloud"].Replace("{provider}", "OpenAI · models.example"), html);
        Assert.DoesNotContain(new Texts(language)["intro.ai.local"], html);
        Assert.DoesNotContain("{provider}", html);
    }

    /// <summary>NFR-SEC-2: the logo comes from the reporter's own origin and refers to nothing else
    /// (its only URL is the SVG namespace, which browsers never fetch).</summary>
    [Fact]
    public async Task The_logo_is_served_by_the_reporter_and_loads_nothing()
    {
        using var app = new ReporterApp();
        var html = await app.Client.GetStringAsync("/");
        Assert.Contains("src=\"/img/logo.svg\"", html);

        var response = await app.Client.GetAsync("/img/logo.svg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("no-store", response.Headers.CacheControl?.ToString() ?? "");
        var svg = await response.Content.ReadAsStringAsync();
        Assert.DoesNotMatch(new Regex(@"<script|href=|src=|url\(|@import", RegexOptions.IgnoreCase), svg);
        Assert.All(Regex.Matches(svg, @"https?://[^""]+"), m => Assert.Equal("http://www.w3.org/2000/svg", m.Value));
    }

    /// <summary>FR-INT-8: a new reporter language has every text the Spanish file has, and no others.</summary>
    [Fact]
    public void Every_reporter_language_has_the_same_texts()
    {
        var assembly = typeof(Texts).Assembly;
        const string prefix = "Testigo.Reporter.Texts.";
        var languages = assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix) && n.EndsWith(".json"))
            .Select(n => n[prefix.Length..^".json".Length]).ToList();
        Assert.Contains("es", languages);
        var spanish = new Texts("es").Keys.ToHashSet();
        foreach (var language in languages)
        {
            Assert.True(spanish.SetEquals(new Texts(language).Keys), $"Reporter texts '{language}' don't have the same keys as 'es'.");
        }
    }

    [Fact]
    public async Task Static_files_are_cacheable_and_have_no_external_urls()
    {
        using var app = new ReporterApp();
        foreach (var path in new[] { "/js/salir.js", "/css/site.css" })
        {
            var response = await app.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("no-store", response.Headers.CacheControl?.ToString() ?? "");
            Assert.DoesNotMatch(new Regex(@"https?://|@import|url\(\s*['""]?//"), await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_report_is_sent_with_the_key_and_nothing_about_the_reporter()
    {
        using var app = new ReporterApp();
        var form = await app.FormFieldsAsync();
        form["a.what_happened"] = Story;
        form["passphrase"] = "";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/reportar") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.UserAgent.ParseAdd("NavegadorDePrueba/1.0");
        var response = await app.Client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
        Assert.Contains("ABCD-EFGH-JKMN-PQRS-TVWX", html);
        Assert.Contains("cielo tranquilo puerta verde", html);

        var sent = Assert.Single(app.Api.Posts);
        Assert.Equal(StubApi.Key, sent.Key);
        Assert.False(sent.Headers.ContainsKey("X-Forwarded-For"));
        Assert.False(sent.Headers.ContainsKey("Forwarded"));
        Assert.Equal("testigo-reporter", sent.Headers["User-Agent"]);
        using var body = System.Text.Json.JsonDocument.Parse(sent.Body);
        Assert.Equal(Story, body.RootElement.GetProperty("answers").GetProperty("what_happened")[0].GetString());
        Assert.Equal("form-1", body.RootElement.GetProperty("form_version").GetString());
        Assert.Contains("\"consents\":[\"store\"]", sent.Body);
        Assert.DoesNotContain("passphrase", sent.Body);
        AssertNoThirdPartyOrInlineCode(html);
    }

    [Fact]
    public async Task Personal_data_is_flagged_before_anything_is_sent()
    {
        using var app = new ReporterApp();
        var form = await app.FormFieldsAsync();
        form["a.what_happened"] = Story + " Pueden escribirme a rosa.ficticia@example.test.";

        var first = await app.Client.PostAsync("/reportar", new FormUrlEncodedContent(form));
        var html = WebUtility.HtmlDecode(await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains(new Texts("es")["pii.heading"], html);
        Assert.Contains("rosa.ficticia@example.test", html); // still in the textarea, to edit
        Assert.Empty(app.Api.Posts);

        form["personal_data_ok"] = "1";
        var second = await app.Client.PostAsync("/reportar", new FormUrlEncodedContent(form));
        Assert.Contains("ABCD-EFGH-JKMN-PQRS-TVWX", await second.Content.ReadAsStringAsync());
        Assert.Single(app.Api.Posts);
    }

    [Fact]
    public async Task When_the_api_is_down_the_text_stays_in_the_form()
    {
        using var app = new ReporterApp(api => api.PostStatus = HttpStatusCode.ServiceUnavailable);
        var form = await app.FormFieldsAsync();
        form["a.what_happened"] = Story;

        var response = await app.Client.PostAsync("/reportar", new FormUrlEncodedContent(form));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(new Texts("es")["unavailable"], html);
        Assert.Contains(Story, html);
        Assert.Contains("value=\"store\" checked", html);
    }

    [Fact]
    public async Task Field_errors_from_the_api_are_shown_next_to_the_field()
    {
        using var app = new ReporterApp(api => api.PostStatus = HttpStatusCode.BadRequest);
        var form = await app.FormFieldsAsync();
        form["a.what_happened"] = Story;

        var response = await app.Client.PostAsync("/reportar", new FormUrlEncodedContent(form));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Elige un país.", html);
        Assert.Contains(Story, html);
        // NFR-UX-1: the invalid field points to the error a screen reader must announce, and a field
        // with nothing to point at gets no empty aria-describedby.
        Assert.DoesNotContain("aria-describedby=\"\"", html);
        Assert.Contains("id=\"f-country-error\"", html);
        var select = Regex.Match(html, @"<select\b[^>]*id=""f-country""[^>]*>").Value;
        Assert.Contains("aria-describedby=\"f-country-error\"", select);
        Assert.Contains("aria-invalid=\"true\"", select);
    }

    [Fact]
    public async Task A_post_without_the_antiforgery_token_is_refused_and_nothing_is_sent()
    {
        using var app = new ReporterApp();
        var response = await app.Client.PostAsync("/reportar", new FormUrlEncodedContent(new Dictionary<string, string> { ["a.what_happened"] = Story, ["consent"] = "store" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(Banner, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Empty(app.Api.Posts);
    }

    [Fact]
    public async Task Submissions_are_rate_limited_per_client()
    {
        using var app = new ReporterApp(settings: new() { ["Reporter:SubmissionsPerClient"] = "2" });
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            var form = await app.FormFieldsAsync();
            form["a.what_happened"] = Story;
            statuses.Add((await app.Client.PostAsync("/reportar", new FormUrlEncodedContent(form))).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(2, app.Api.Posts.Count);
        // Reading the form is never limited.
        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync("/reportar")).StatusCode);
    }

    [Fact]
    public async Task Logs_never_contain_what_the_reporter_typed()
    {
        using var app = new ReporterApp();
        var form = await app.FormFieldsAsync();
        form["a.what_happened"] = Story;
        form["contact_method"] = "email";
        form["contact_value"] = "persona.inventada@example.test";
        await app.Client.PostAsync("/reportar", new FormUrlEncodedContent(form));

        Assert.Single(app.Api.Posts);
        var logs = string.Join("\n", app.Logs);
        Assert.DoesNotContain("préstamos", logs);
        Assert.DoesNotContain("persona.inventada", logs);
        Assert.DoesNotContain("ABCD-EFGH", logs);
    }

    [Fact]
    public async Task The_quick_exit_link_points_to_the_configured_page()
    {
        using var app = new ReporterApp(settings: new() { ["Reporter:QuickExitUrl"] = "https://clima.example.test/" });
        var html = await app.Client.GetStringAsync("/");
        Assert.Contains("id=\"salir\"", html);
        Assert.Contains("href=\"https://clima.example.test/\"", html);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var v) ? string.Join(",", v) :
        response.Content.Headers.TryGetValues(name, out var c) ? string.Join(",", c) : "";

    /// <summary>NFR-SEC-2 and NFR-SEC-6: only same-origin scripts and styles, no inline code or styles.</summary>
    private static void AssertNoThirdPartyOrInlineCode(string html)
    {
        foreach (Match m in Regex.Matches(html, @"<script\b[^>]*>(?<body>.*?)</script>", RegexOptions.Singleline))
        {
            Assert.Matches(new Regex(@"\bsrc=""/[^/]"), m.Value);
            Assert.True(string.IsNullOrWhiteSpace(m.Groups["body"].Value), "inline script");
        }

        Assert.DoesNotMatch(new Regex(@"\b(src|srcset|poster|data)=""(https?:)?//", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"<link\b[^>]*href=""(https?:)?//", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"<form\b[^>]*action=""(https?:)?//", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"<style\b|\sstyle=""|\son[a-z]+=""", RegexOptions.IgnoreCase), html);
    }

    /// <summary>The reporter app with its HTTP client pointed at <see cref="StubApi"/>.</summary>
    private sealed class ReporterApp : IDisposable
    {
        private readonly WebApplicationFactory<Program> factory;
        public StubApi Api { get; } = new();
        public ConcurrentQueue<string> Logs { get; } = new();
        public HttpClient Client { get; }

        public ReporterApp(Action<StubApi>? configure = null, Dictionary<string, string?>? settings = null)
        {
            configure?.Invoke(Api);
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.UseSetting("Reporter:ApiBaseUrl", "http://api.test");
                b.UseSetting("Reporter:IntakeKey", StubApi.Key);
                foreach (var (k, v) in settings ?? []) b.UseSetting(k, v);
                b.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Trace).AddProvider(new ListLoggerProvider(Logs)));
                b.ConfigureTestServices(s => s.AddHttpClient<IntakeClient>().ConfigurePrimaryHttpMessageHandler(() => Api));
            });
            Client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        /// <summary>GETs the form and returns its antiforgery token plus valid answers.</summary>
        public async Task<Dictionary<string, string>> FormFieldsAsync(string path = "/reportar")
        {
            var html = await Client.GetStringAsync(path);
            var input = Regex.Match(html, @"<input\b[^>]*name=""__RequestVerificationToken""[^>]*>").Value;
            var token = WebUtility.HtmlDecode(Regex.Match(input, @"value=""([^""]+)""").Groups[1].Value);
            Assert.NotEmpty(token);
            return new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["a.country"] = "CL",
                ["consent"] = "store",
                ["contact_method"] = "none",
            };
        }

        public void Dispose()
        {
            Client.Dispose();
            factory.Dispose();
        }
    }

    private sealed record SentRequest(string? Key, Dictionary<string, string> Headers, string Body);

    /// <summary>Answers like the API's intake endpoints, with synthetic data.</summary>
    private sealed class StubApi : HttpMessageHandler
    {
        public const string Key = "clave-de-prueba-0123456789abcdefghij";
        public HttpStatusCode PostStatus { get; set; } = HttpStatusCode.OK;
        public string AiMode { get; set; } = "mock";
        public ConcurrentQueue<SentRequest> PostsQueue { get; } = new();
        public List<SentRequest> Posts => [.. PostsQueue];
        public ConcurrentQueue<string> FormQueries { get; } = new();

        private const string Contacts = """{"CL":{"emergency":[{"label":"Fono de prueba","phone":"000 000"}],"support":[{"label":"Apoyo ficticio","phone":"111 111"}],"response_time":"10 días hábiles"}}""";

        private const string Form = """
        {"version":"form-1","notice_version":"aviso-1","language":"es",
         "fields":[
          {"key":"what_happened","kind":"longtext","required":true,"max_length":8000,"label":"¿Qué pasó?","help":"Cuenta con tus palabras.","options":[]},
          {"key":"technology","kind":"multichoice","required":false,"max_length":0,"label":"Tecnología","options":[{"key":"chatbot","label":"Chatbot"},{"key":"scoring","label":"Puntaje automático"}]},
          {"key":"country","kind":"choice","required":true,"max_length":0,"label":"País","options":[{"key":"CL","label":"Chile"},{"key":"AR","label":"Argentina"}],"default":"CL"},
          {"key":"region","kind":"choice","required":false,"max_length":0,"label":"Región","options":[{"key":"RM","label":"Metropolitana"}],"only_when":{"country":"CL"}}
         ],
         "consents":[{"key":"store","required":true,"label":"Acepto que se guarde mi reporte."},{"key":"research","required":false,"label":"Acepto que se use, sin datos personales, en investigación."}],
         "contact_methods":[{"key":"none","label":"No quiero que me contacten"},{"key":"email","label":"Correo"}],
         "passphrase_min_length":12,
         "contacts":
        """ + Contacts + """
        ,"ai_mode":"mock"}
        """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Headers.TryGetValues(IntakeClient.KeyHeader, out var k) ? k.Single() : null;
            if (key != Key) return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/intake/form")
            {
                FormQueries.Enqueue(request.RequestUri.Query);
                var form = Form.Replace("\"ai_mode\":\"mock\"", $"\"ai_mode\":\"{AiMode}\",\"ai_provider\":\"OpenAI\",\"ai_endpoint\":\"models.example\"");
                // The API answers with the form in the language asked for (FR-INT-8).
                return Json(HttpStatusCode.OK, request.RequestUri.Query.Contains("lang=en", StringComparison.Ordinal)
                    ? form.Replace("\"language\":\"es\"", "\"language\":\"en\"").Replace("¿Qué pasó?", "What happened?").Replace("Acepto que se guarde mi reporte.", "I agree to my report being stored.")
                    : form);
            }

            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/intake/reports")
            {
                var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(" ", h.Value), StringComparer.OrdinalIgnoreCase);
                PostsQueue.Enqueue(new SentRequest(key, headers, await request.Content!.ReadAsStringAsync(cancellationToken)));
                return PostStatus switch
                {
                    HttpStatusCode.OK => Json(HttpStatusCode.OK, """{"case_code":"ABCD-EFGH-JKMN-PQRS-TVWX","generated_passphrase":"cielo tranquilo puerta verde","immediate_danger":false,"contacts":""" + Contacts.Replace("{\"CL\":", "")[..^1] + ""","response_time":"10 días hábiles"}"""),
                    HttpStatusCode.BadRequest => Json(HttpStatusCode.BadRequest, """{"errors":[{"field":"country","message":"Elige un país."}]}"""),
                    _ => new HttpResponseMessage(PostStatus),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class ListLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, sink);
        public void Dispose() { }

        private sealed class ListLogger(string category, ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}

/// <summary>
/// The Dockerfiles copy every project the published app references, directly or not, so
/// <c>docker compose up</c> builds on a clean machine (§12). A missing COPY only shows up there.
/// </summary>
public sealed class DockerfileTests
{
    [Theory]
    [InlineData("docker/Dockerfile.reporter", "src/Testigo.Reporter/Testigo.Reporter.csproj")]
    [InlineData("docker/Dockerfile.api", "src/AgentRuntime.Api/AgentRuntime.Api.csproj")]
    public void Every_referenced_project_is_copied(string dockerfile, string project)
    {
        var root = RutTests.RepoRoot();
        var text = File.ReadAllText(Path.Combine(root, dockerfile));
        foreach (var dir in Closure(root, project))
        {
            var name = Path.GetFileName(dir);
            Assert.Contains($"COPY src/{name}/{name}.csproj src/{name}/", text);
            Assert.Contains($"COPY src/{name}/ src/{name}/", text);
        }
    }

    /// <summary>Key files on a developer machine must never reach an image (NFR-SEC-4).</summary>
    [Fact]
    public void Build_context_excludes_key_files()
    {
        var lines = File.ReadAllLines(Path.Combine(RutTests.RepoRoot(), ".dockerignore"))
            .Select(l => l.Trim())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("**/keys/", lines);
        Assert.Contains("**/*.key", lines);
        Assert.Contains(".testigo-dev/", lines);
        Assert.Contains("**/.testigo-dev/", lines);
    }

    private static HashSet<string> Closure(string root, string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([Path.GetFullPath(Path.Combine(root, project))]);
        while (queue.TryDequeue(out var path))
        {
            if (!seen.Add(Path.GetDirectoryName(path)!)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"ProjectReference Include=""([^""]+)"""))
            {
                queue.Enqueue(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, m.Groups[1].Value.Replace('\\', '/'))));
            }
        }

        return seen;
    }
}
