using AgentRuntime.Api.Platform;
using Microsoft.AspNetCore.Mvc;
using Testigo.Infrastructure.Services;
using Testigo.Taxonomy;

namespace AgentRuntime.Api.Controllers;

/// <summary>
/// The analyst queue and case view (FR-CASE-1 to FR-CASE-4). Roles are checked on every request
/// (NFR-SEC-8): only signed-in people with the Analyst or Reviewer role, with two-factor sign-in
/// on, reach case data. A case of another organization is a 404.
/// </summary>
[ApiController]
[Route("api/testigo")]
public sealed class TestigoCasesController(StaffAccess access, CaseService cases, HarmTaxonomy taxonomy) : ControllerBase
{
    public sealed record SuggestionDecision(string Action, string? Term, string? Value);
    public sealed record StatusRequest(string To, string? Reason);
    public sealed record ClassificationRequest(string Dimension, string Term);
    public sealed record LinkDecision(string Action);
    public sealed record AssignRequest(string? AssigneeId);
    public sealed record ReasonRequest(string? Reason);
    public sealed record RoleRequest(string UserId, string Role, bool Granted);

    /// <summary>Who the caller is to Testigo, so the dashboard can explain a missing role or 2FA.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var (staff, problem) = await access.ResolveAsync(HttpContext.Caller(), cancellationToken, requireCaseRole: false);
        return Ok(new
        {
            allowed = staff is { CanReadCases: true },
            problem = problem switch { AccessProblem.TwoFactorRequired => "two_factor_required", AccessProblem.NotAPerson => "not_a_person", _ => staff is { CanReadCases: true } ? null : "no_role" },
            roles = staff?.Roles.Order().ToList() ?? [],
            organization_admin = staff?.IsOrganizationAdmin ?? false,
        });
    }

    [HttpGet("taxonomy")]
    public async Task<IActionResult> Taxonomy(CancellationToken cancellationToken)
    {
        if (await Staff(cancellationToken) is not { } _) return Denied();
        return Ok(new
        {
            version = taxonomy.Version,
            status = taxonomy.Status,
            dimensions = taxonomy.Dimensions.Select(d => new
            {
                key = d.Key,
                label = d.Labels.GetValueOrDefault("es") ?? d.Key,
                multiple = d.Multiple,
                terms = d.Terms.Select(t => new { key = t.Key, label = t.Labels.GetValueOrDefault("es") ?? t.Key, description = t.Description.GetValueOrDefault("es") }),
            }),
        });
    }

    [HttpGet("cases")]
    public async Task<IActionResult> Queue([FromQuery] string? status, [FromQuery] string? term, [FromQuery] string? country, [FromQuery] string? assignee,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] bool danger = false, [FromQuery] int limit = 50, [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (await Staff(cancellationToken) is not { } staff) return Denied();
        var page = await cases.QueueAsync(staff, new QueueFilter { Status = status, Term = term, Country = country, Assignee = assignee, From = from, To = to, DangerOnly = danger, Limit = limit, Offset = offset }, cancellationToken);
        return Ok(new { items = page.Items, total = page.Total });
    }

    [HttpGet("cases/{id:guid}")]
    public async Task<IActionResult> Case(Guid id, [FromQuery] string? reason, CancellationToken cancellationToken)
    {
        if (await Staff(cancellationToken) is not { } staff) return Denied();
        return From(await cases.GetAsync(staff, id, reason, cancellationToken));
    }

    [HttpPost("cases/{id:guid}/suggestions/{suggestionId:guid}")]
    public async Task<IActionResult> Decide(Guid id, Guid suggestionId, [FromBody] SuggestionDecision body, CancellationToken cancellationToken)
    {
        if (await Staff(cancellationToken) is not { } staff) return Denied();
        return From(await cases.DecideSuggestionAsync(staff, id, suggestionId, body.Action, body.Term, body.Value, cancellationToken));
    }

    [HttpPost("cases/{id:guid}/classifications")]
    public async Task<IActionResult> Classify(Guid id, [FromBody] ClassificationRequest body, CancellationToken cancellationToken)
    {
        if (await Staff(cancellationToken) is not { } staff) return Denied();
        var result = await cases.AddClassificationAsync(staff, id, body.Dimension, body.Term, cancellationToken);
        return result.Error == CaseError.None ? Ok(new { id = result.Value }) : From(result);
    }

    [HttpPost("cases/{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, [FromBody] StatusRequest body, CancellationToken cancellationToken)
    {
        if (await Staff(cancellationToken) is not { } staff) return Denied();
        var result = await cases.ChangeStatusAsync(staff, id, body.To, body.Reason, cancellationToken);
        return result.Error == CaseError.None ? Ok(new { status = result.Value }) : From(result);
    }

    [HttpPost("cases/{id:guid}/links/{linkId:guid}")]
    public async Task<IActionResult> Link(Guid id, Guid linkId, [FromBody] LinkDecision body, CancellationToken cancellationToken)
    {
        if (await Staff(cancellationToken) is not { } staff) return Denied();
        return From(await cases.DecideLinkAsync(staff, id, linkId, body.Action, cancellationToken));
    }

    [HttpPost("cases/{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignRequest body, CancellationToken cancellationToken)
    {
        if (await Staff(cancellationToken) is not { } staff) return Denied();
        return From(await cases.AssignAsync(staff, id, string.IsNullOrWhiteSpace(body.AssigneeId) ? null : body.AssigneeId, cancellationToken));
    }

    /// <summary>The reporter's contact details: a separate read with a reason, recorded.</summary>
    [HttpPost("cases/{id:guid}/contact")]
    public async Task<IActionResult> Contact(Guid id, [FromBody] ReasonRequest body, CancellationToken cancellationToken)
    {
        if (await Staff(cancellationToken) is not { } staff) return Denied();
        var result = await cases.RevealContactAsync(staff, id, body.Reason, cancellationToken);
        return result.Error == CaseError.None ? Ok(new { method = result.Value.Method, value = result.Value.Value }) : From(result);
    }

    /// <summary>Who read or changed the case (NFR-SEC-10). Reviewers only.</summary>
    [HttpGet("cases/{id:guid}/access")]
    public async Task<IActionResult> AccessLog(Guid id, CancellationToken cancellationToken)
    {
        if (await Staff(cancellationToken) is not { } staff) return Denied();
        var result = await cases.AccessLogAsync(staff, id, cancellationToken);
        return result.Error == CaseError.None ? Ok(new { items = result.Value }) : From(result);
    }

    /// <summary>Members and their Testigo roles. Organization admins and reviewers.</summary>
    [HttpGet("staff")]
    public async Task<IActionResult> StaffList(CancellationToken cancellationToken)
    {
        var (staff, problem) = await access.ResolveAsync(HttpContext.Caller(), cancellationToken, requireCaseRole: false);
        if (staff is null) return Denied(problem);
        if (!staff.IsOrganizationAdmin && !staff.IsReviewer) return StatusCode(StatusCodes.Status403Forbidden, new { error = "Solo administración o revisión." });
        return Ok(new { items = await cases.StaffAsync(staff.OrganizationId, cancellationToken) });
    }

    [HttpPost("staff/roles")]
    public async Task<IActionResult> SetRole([FromBody] RoleRequest body, CancellationToken cancellationToken)
    {
        var (staff, problem) = await access.ResolveAsync(HttpContext.Caller(), cancellationToken, requireCaseRole: false);
        if (staff is null) return Denied(problem);
        return From(await cases.SetRoleAsync(staff, body.UserId, body.Role, body.Granted, cancellationToken));
    }

    private AccessProblem lastProblem;

    private async Task<StaffContext?> Staff(CancellationToken cancellationToken)
    {
        var (staff, problem) = await access.ResolveAsync(HttpContext.Caller(), cancellationToken);
        lastProblem = problem;
        return staff;
    }

    private IActionResult Denied() => Denied(lastProblem);

    private ObjectResult Denied(AccessProblem problem) => StatusCode(StatusCodes.Status403Forbidden, new
    {
        error = problem switch
        {
            AccessProblem.TwoFactorRequired => "Activa la verificación en dos pasos para trabajar con casos.",
            AccessProblem.NotAPerson => "Los casos solo se abren con una sesión personal, no con claves de API.",
            _ => "Necesitas el rol de Análisis o Revisión para trabajar con casos.",
        },
        code = problem switch { AccessProblem.TwoFactorRequired => "two_factor_required", AccessProblem.NotAPerson => "not_a_person", _ => "no_role" },
    });

    private IActionResult From<T>(CaseResult<T> result) => result.Error switch
    {
        CaseError.None => result.Value is bool ? Ok(new { ok = true }) : Ok(result.Value),
        CaseError.NotFound => NotFound(new { error = result.Message }),
        CaseError.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.Message }),
        CaseError.Conflict => Conflict(new { error = result.Message }),
        _ => BadRequest(new { error = result.Message }),
    };
}
