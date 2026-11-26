using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Testigo.Reporter.Pages;

/// <summary>A plain error page: no exception details, nothing the reporter typed.</summary>
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    public string Heading { get; private set; } = "error.heading";
    public string Lead { get; private set; } = "error.lead";

    public void OnGet(int? code) => Show(code);
    public void OnPost(int? code) => Show(code);

    private void Show(int? code)
    {
        // Only a re-executed status (404, 400) keeps its code; a direct visit or an exception is a 500.
        var status = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>() is not null && code is >= 400 and < 600
            ? code.Value
            : StatusCodes.Status500InternalServerError;
        Response.StatusCode = status;
        (Heading, Lead) = status switch
        {
            404 => ("notfound.heading", "notfound.lead"),
            400 => ("error.heading", "expired.lead"),
            _ => ("error.heading", "error.lead"),
        };
    }
}
