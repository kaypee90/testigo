using Microsoft.AspNetCore.Mvc.RazorPages;
using Testigo.Redaction;

namespace Testigo.Reporter.Pages;

/// <summary>
/// The report form (FR-INT-1, FR-INT-3), its personal-data warning, and the acknowledgement
/// (FR-INT-9). Works without JavaScript. The acknowledgement is the POST's own response, so the
/// case code never appears in a URL, and the page isn't cached.
/// </summary>
public sealed class ReportarModel(IntakeClient intake, Redactor redactor, SubmissionLimiter limiter, Texts texts) : PageModel
{
    public const string FreeText = Testigo.Forms.FormDefinition.FreeTextField;

    public FormDto? Form { get; private set; }
    public Dictionary<string, List<string>> Values { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Consents { get; } = new(StringComparer.Ordinal);
    public string? ContactMethod { get; private set; }
    public string? ContactValue { get; private set; }
    public Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);
    public IReadOnlyList<string> PersonalData { get; private set; } = [];
    public string? Notice { get; private set; }
    public ReceiptDto? Receipt { get; private set; }
    public bool PassphraseWasChosen { get; private set; }

    public string Value(string key) => Values.GetValueOrDefault(key)?.FirstOrDefault() ?? string.Empty;
    public bool Has(string key, string option) => Values.GetValueOrDefault(key)?.Contains(option) == true;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Form = await intake.FormAsync(texts.Language, cancellationToken);
        if (Form is null)
        {
            Notice = texts["unavailable.form"];
            return;
        }

        foreach (var f in Form.Fields.Where(f => f.Default is not null)) Values[f.Key] = [f.Default!];
    }

    public async Task OnPostAsync(CancellationToken cancellationToken)
    {
        Form = await intake.FormAsync(texts.Language, cancellationToken);
        Read();
        if (Form is null)
        {
            Notice = texts["unavailable"];
            return;
        }

        // The warning before sending (FR-INT-3): run here, on this server, before the text goes anywhere.
        var text = Value(FreeText);
        if (Request.Form["personal_data_ok"] != "1" && text.Length > 0)
        {
            PersonalData = redactor.Detect(text);
            if (PersonalData.Count > 0) return;
        }

        if (!limiter.TryAcquire(HttpContext.Connection.RemoteIpAddress))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Notice = texts["toomany"];
            return;
        }

        var passphrase = Request.Form["passphrase"].ToString();
        var result = await intake.SubmitAsync(new SubmissionDto
        {
            FormVersion = Form.Version,
            Answers = Values,
            Consents = [.. Consents],
            ContactMethod = ContactMethod,
            ContactValue = ContactValue,
            Passphrase = string.IsNullOrWhiteSpace(passphrase) ? null : passphrase,
            Language = texts.Language,
        }, cancellationToken);

        switch (result.Outcome)
        {
            case SubmitOutcome.Received:
                Receipt = result.Receipt;
                PassphraseWasChosen = !string.IsNullOrWhiteSpace(passphrase);
                break;
            case SubmitOutcome.Invalid:
                foreach (var e in result.Errors!) Errors.TryAdd(e.Field, e.Message);
                Response.StatusCode = StatusCodes.Status400BadRequest;
                break;
            default:
                Notice = texts["unavailable"];
                Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                break;
        }
    }

    /// <summary>Everything the reporter sent, so nothing is lost when the page comes back with a warning or an error.</summary>
    private void Read()
    {
        foreach (var (key, values) in Request.Form)
        {
            if (!key.StartsWith("a.", StringComparison.Ordinal)) continue;
            var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
            if (list.Count > 0) Values[key[2..]] = list;
        }

        foreach (var c in Request.Form["consent"]) if (!string.IsNullOrEmpty(c)) Consents.Add(c);
        ContactMethod = NullIfBlank(Request.Form["contact_method"]);
        ContactValue = NullIfBlank(Request.Form["contact_value"]);
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
