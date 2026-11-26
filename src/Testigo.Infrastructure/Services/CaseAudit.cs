using System.Text.Json;
using AgentRuntime.Safety;

namespace Testigo.Infrastructure.Services;

/// <summary>
/// Testigo's entries in the hash-chained audit log (NFR-SEC-10): one chain per organization.
/// Entries name the actor, the case and the action; they never contain report content, contact
/// details or case codes (NFR-OPS-4).
/// </summary>
public sealed class CaseAudit(IAuditLog log)
{
    public static string Scope(string organizationId) => $"testigo:{organizationId}";
    public static string Target(Guid caseId) => $"case:{caseId:N}";

    public Task RecordAsync(string organizationId, Actor actor, string action, Guid? caseId, string summary, object? detail = null, string? key = null, string outcome = "ok", CancellationToken cancellationToken = default) =>
        log.AppendAsync(new AuditEntry
        {
            Scope = Scope(organizationId),
            Key = key ?? $"{action}:{Guid.NewGuid():N}",
            ActorType = actor.Type,
            ActorId = actor.Id,
            ActorName = actor.Name,
            Action = action,
            Target = caseId is { } id ? Target(id) : string.Empty,
            Outcome = outcome,
            Summary = summary,
            DetailJson = detail is null ? "{}" : JsonSerializer.Serialize(detail, AuditJson),
        }, cancellationToken);

    public Task<IReadOnlyList<AuditEntry>> ForCaseAsync(string organizationId, Guid caseId, string? actionPrefix, int limit, CancellationToken cancellationToken) =>
        log.QueryAsync(new AuditQuery { Scope = Scope(organizationId), Text = Target(caseId), ActionPrefix = actionPrefix, Limit = Math.Clamp(limit, 1, 500) }, cancellationToken);

    private static readonly JsonSerializerOptions AuditJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

/// <summary>Who did something: a staff member, the system, or an anonymous reporter.</summary>
public sealed record Actor(string Type, string Id, string Name)
{
    public static readonly Actor System = new("system", "testigo", "Testigo");
    public static readonly Actor Reporter = new("reporter", "anonymous", "Persona que reporta");
    public static Actor Staff(string userId, string? email) => new("user", userId, email ?? userId);
}
