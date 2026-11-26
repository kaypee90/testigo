using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Testigo.Reporter.Pages;

/// <summary>What happens to a report, before the form (FR-INT-2).</summary>
public sealed class IndexModel(IntakeClient intake, Texts texts) : PageModel
{
    public IReadOnlyList<ContactDto> Emergency { get; private set; } = [];
    public string AiDescription { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var form = await intake.FormAsync(texts.Language, cancellationToken);
        Emergency = form?.ContactsFor("CL").Emergency ?? [];
        AiDescription = texts[$"intro.ai.{(form?.AiMode is "mock" or "cloud" ? form.AiMode : "local")}"];
    }
}
