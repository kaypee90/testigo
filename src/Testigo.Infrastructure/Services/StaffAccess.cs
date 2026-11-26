using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.Infrastructure.Persistence;
using AgentRuntime.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testigo.Infrastructure.Persistence;

namespace Testigo.Infrastructure.Services;

/// <summary>Testigo's staff roles (§3, NFR-SEC-8). They are separate from the organization roles: an Admin can't read report content without the Analyst role.</summary>
public static class StaffRoles
{
    public const string Analyst = "analyst";
    public const string Reviewer = "reviewer";
    public const string IntakeAdvocate = "intake_advocate";
    public static readonly IReadOnlyList<string> All = [Analyst, Reviewer, IntakeAdvocate];
}

/// <summary>A staff member allowed into Testigo, with their roles.</summary>
public sealed record StaffContext(string OrganizationId, string UserId, string? Email, TenantRole OrganizationRole, IReadOnlySet<string> Roles)
{
    /// <summary>Analysts read and work cases; Reviewers do too, and verify.</summary>
    public bool CanReadCases => Roles.Contains(StaffRoles.Analyst) || Roles.Contains(StaffRoles.Reviewer);
    public bool IsReviewer => Roles.Contains(StaffRoles.Reviewer);
    public bool IsOrganizationAdmin => OrganizationRole >= TenantRole.Admin;
    public Actor Actor => Actor.Staff(UserId, Email);
}

public enum AccessProblem
{
    None,
    /// <summary>API keys and agents never reach case data: only signed-in people.</summary>
    NotAPerson,
    TwoFactorRequired,
    NoRole,
}

/// <summary>Resolves the signed-in caller to a staff context, checked on every request (NFR-SEC-8).</summary>
public sealed class StaffAccess(IDbContextFactory<TestigoDbContext> testigo, IDbContextFactory<AgentDbContext> platform, IOptions<TestigoOptions> options)
{
    public async Task<(StaffContext? Staff, AccessProblem Problem)> ResolveAsync(Caller caller, CancellationToken cancellationToken, bool requireCaseRole = true)
    {
        if (caller.UserId is null) return (null, AccessProblem.NotAPerson);
        if (options.Value.Staff.RequireTwoFactor)
        {
            await using var p = await platform.CreateDbContextAsync(cancellationToken);
            var twoFactor = await p.Users.AsNoTracking().Where(u => u.UserId == caller.UserId).Select(u => u.TwoFactorEnabledAt).FirstOrDefaultAsync(cancellationToken);
            if (twoFactor is null) return (null, AccessProblem.TwoFactorRequired);
        }

        await using var db = await testigo.CreateDbContextAsync(cancellationToken);
        var roles = (await db.StaffRoles.AsNoTracking().Where(r => r.OrganizationId == caller.TenantId && r.UserId == caller.UserId).Select(r => r.Role).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var staff = new StaffContext(caller.TenantId, caller.UserId, caller.Email, caller.Role, roles);
        if (requireCaseRole && !staff.CanReadCases) return (null, AccessProblem.NoRole);
        return (staff, AccessProblem.None);
    }
}
