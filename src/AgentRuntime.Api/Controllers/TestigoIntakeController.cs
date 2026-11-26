using AgentRuntime.Api.Platform;
using AgentRuntime.LLM;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Testigo.Forms;
using Testigo.Infrastructure;
using Testigo.Infrastructure.Services;
using Testigo.Infrastructure.Triage;
using Testigo.Support;
using Testigo.Taxonomy;

namespace AgentRuntime.Api.Controllers;

/// <summary>
/// The intake endpoints, for the reporter app only (NFR-SEC-7): every request must carry the
/// shared key, and anything else gets a 404. These requests are never logged, traced or recorded
/// with an address (NFR-SEC-3, see <see cref="ReporterPaths"/>).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/intake")]
[EnableRateLimiting("intake")]
public sealed class TestigoIntakeController(IntakeGate gate, IntakeService intake, HarmTaxonomy taxonomy, SupportContacts contacts, Testigo.Security.PassphraseHashOptions passphraseOptions, IConfiguration configuration) : ControllerBase
{
    /// <summary>
    /// The form as data and the support contacts, with their words in <paramref name="lang"/>
    /// (FR-INT-8): Spanish by default, or a language the form has a translation for.
    /// </summary>
    [HttpGet("form")]
    public IActionResult Form([FromQuery] string? lang)
    {
        if (!Allowed()) return NotFound();
        var form = intake.Form.Supports(lang) ? intake.Form.In(lang) : intake.Form;
        var aiMode = AiMode(configuration["Llm:Provider"], configuration["Llm:BaseUrl"], configuration["Llm:Model"]);
        return Ok(new
        {
            version = form.Version,
            notice_version = form.NoticeVersion,
            language = form.Language,
            languages = intake.Form.Languages,
            fields = form.Fields.Select(f => new
            {
                key = f.Key,
                kind = f.Kind.ToString().ToLowerInvariant(),
                required = f.Required,
                max_length = f.MaxLength,
                label = f.Label,
                help = f.Help,
                options = form.OptionsFor(f, taxonomy, form.Language).Select(o => new { key = o.Key, label = o.Label }),
                @default = f.Default,
                only_when = f.OnlyWhen,
            }),
            consents = form.Consents.Select(c => new { key = c.Key, required = c.Required, label = c.Label }),
            contact_methods = form.ContactMethods.Select(m => new { key = m.Key, label = m.Label }),
            passphrase_min_length = passphraseOptions.MinimumLength,
            contacts = contacts.Countries.ToDictionary(c => c.Key, c => c.Value.In(form.Language)),
            ai_mode = aiMode,
            ai_provider = LlmOptions.CanonicalProvider(configuration["Llm:Provider"]),
            ai_endpoint = PublicEndpoint(aiMode, configuration["Llm:BaseUrl"]),
        });
    }

    /// <summary>
    /// Where the triage model runs, so the reporter page can say so truthfully (FR-INT-2,
    /// NFR-AI-1): "local" for a model on our own servers, "mock" for the scripted demo model,
    /// "cloud" for anything else.
    /// </summary>
    internal static string AiMode(string? provider, string? baseUrl = null, string? model = null)
    {
        if (LlmOptions.CanonicalProvider(provider) == "Mock") return "mock";
        try
        {
            CaseModelPolicy.Validate(provider, baseUrl, model, localOnly: true);
            return "local";
        }
        catch (InvalidOperationException)
        {
            return "cloud";
        }
    }

    /// <summary>
    /// The model endpoint's host for the reporter's notice, only when text leaves for a hosted
    /// service (FR-INT-2). A local server's address (a private IP, "ollama", a container host) stays
    /// private: the reporter page is public. Host only, never user info, path, query or credentials.
    /// </summary>
    internal static string? PublicEndpoint(string aiMode, string? baseUrl) =>
        aiMode == "cloud" && Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) ? endpoint.Host : null;

    /// <summary>Receives a report. 400 with field errors when it doesn't validate; nothing is stored then.</summary>
    [HttpPost("reports")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Submit([FromBody] ReportSubmission submission, CancellationToken cancellationToken)
    {
        if (!Allowed()) return NotFound();
        var result = await intake.SubmitAsync(submission, "public", cancellationToken);
        if (result.Receipt is not { } receipt) return BadRequest(new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) });
        return Ok(new
        {
            case_code = receipt.CaseCode,
            generated_passphrase = receipt.GeneratedPassphrase,
            immediate_danger = receipt.ImmediateDanger,
            contacts = receipt.Contacts,
            response_time = receipt.ResponseTime,
        });
    }

    private bool Allowed() => gate.Allows(Request.Headers[IntakeGate.HeaderName].FirstOrDefault());
}
