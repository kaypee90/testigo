using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.Infrastructure.Persistence;
using AgentRuntime.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace AgentRuntime.Api.Platform;

/// <summary>
/// A person deleting their own account (Settings → Account → Delete account). Organizations they
/// share with others are left (an organization they're the only owner of has to get another owner
/// first). Organizations nobody else belongs to are closed with the account: their invitations stop
/// working and the organization is suspended. Their cases and audit log are kept, untouched, for a
/// platform operator to deal with: a person leaving never erases reports.
/// </summary>
public sealed class AccountDeletionService(
    IDbContextFactory<AgentDbContext> dbFactory,
    IdentityService identity,
    ILogger<AccountDeletionService> logger)
{
    public const string ClosedReason = "Closed: its only member deleted their account";

    public sealed record Outcome(bool Success, string? Error, int OrganizationsLeft = 0, int OrganizationsClosed = 0)
    {
        public static Outcome Fail(string error) => new(false, error);
    }

    public async Task<Outcome> DeleteAsync(string userId, string password, CancellationToken ct)
    {
        var plan = await identity.PlanAccountDeletionAsync(userId, ct);
        if (plan is null) return Outcome.Fail("No such account.");
        if (plan.IsOperator)
        {
            return Outcome.Fail("This is a platform operator's account. Remove its email from the server's operators (PLATFORM_ADMIN_EMAIL) before deleting it.");
        }

        if (plan.Blocks.Count > 0)
        {
            return Outcome.Fail($"You're the only owner of {string.Join(", ", plan.Blocks.Select(b => $"\"{b.TenantName}\""))}. " +
                "Make another member an owner, or remove the other members, then try again.");
        }

        if (!await identity.VerifyPasswordAsync(userId, password, ct)) return Outcome.Fail("The password is wrong.");

        foreach (var org in plan.Closes)
        {
            await CloseAsync(org.TenantId, ct);
        }

        await identity.DeleteUserAsync(userId, ct);
        return new Outcome(true, null, plan.Leaves.Count, plan.Closes.Count);
    }

    /// <summary>Suspends the organization and ends its open invitations.</summary>
    private async Task CloseAsync(string tenantId, CancellationToken ct)
    {
        var tenant = TenantIds.Normalize(tenantId);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var now = DateTimeOffset.UtcNow;
            await db.Invitations.Where(i => i.TenantId == tenant && i.AcceptedAt == null && i.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, now), ct);
            await db.Tenants.Where(t => t.TenantId == tenant).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TenantStatuses.Suspended)
                .SetProperty(t => t.SuspendedAt, now)
                .SetProperty(t => t.SuspendedReason, ClosedReason), ct);
        }

        logger.LogWarning("Organization {TenantId} was closed with its only member's account", tenant);
    }
}
