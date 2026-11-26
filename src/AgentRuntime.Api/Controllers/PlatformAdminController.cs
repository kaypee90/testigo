using AgentRuntime.Api.Platform;
using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.Infrastructure.Persistence;
using AgentRuntime.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentRuntime.Api.Controllers;

/// <summary>
/// The platform admin portal (docs/platform.md#platform-administration): every organization on the
/// server, their people and suspension. Operators only (Auth:PlatformAdmins
/// or the local admin), signed in from a browser.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policies.PlatformAdmin)]
public sealed class PlatformAdminController(
    PlatformAdminService admin,
    PlatformMailer mailer,
    BuildInfo build) : ControllerBase
{
    public sealed record CreateOrganizationBody(string Name, string OwnerEmail);
    public sealed record UpdateOrganizationBody(string? Name);
    public sealed record SuspendBody(string? Reason);
    public sealed record AddMemberBody(string Email, string? Role);
    public sealed record RoleBody(string Role);
    public sealed record EmailBody(string Email);

    private string Operator => HttpContext.Caller().Email ?? HttpContext.Caller().ActorId;

    /// <summary>Counts for the portal's overview and which build of the server is running.</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var (organizations, suspended, members) = await admin.CountsAsync(ct);
        return Ok(new
        {
            organizations,
            active_organizations = organizations - suspended,
            suspended_organizations = suspended,
            members,
            email_enabled = mailer.Enabled,
            version = new
            {
                commit = build.Commit,
                short_commit = build.ShortCommit,
                built_at = build.BuiltAt,
                started_at = build.StartedAt
            },
            recent_activity = (await admin.RecentActivityAsync(15, ct)).Select(e => new { at = e.At, actor = e.ActorName, action = e.Action, target = e.Target, summary = e.Summary })
        });
    }

    // ---- Organizations ------------------------------------------------------------------

    [HttpGet("organizations")]
    public async Task<IActionResult> Organizations([FromQuery] string? q, [FromQuery] string? status, CancellationToken ct) =>
        Ok((await admin.ListAsync(q, status, ct)).Select(ToJson));

    [HttpPost("organizations")]
    public async Task<IActionResult> Create([FromBody] CreateOrganizationBody body, CancellationToken ct)
    {
        var result = await admin.CreateAsync(body.Name, body.OwnerEmail, Operator, ct);
        if (!result.Success) return BadRequest(new { error = result.Error });
        var (tenantId, owner) = result.Value;
        return Ok(new { tenant_id = tenantId, owner = ToJson(owner) });
    }

    [HttpGet("organizations/{tenantId}")]
    public async Task<IActionResult> Get(string tenantId, CancellationToken ct)
    {
        if (await admin.GetAsync(tenantId, ct) is not { } detail) return NotFound();
        return Ok(new
        {
            organization = ToJson(detail.Summary),
            members = detail.Members.Select(m => new { user_id = m.UserId, email = m.Email, name = m.Name, role = m.Role.ToString(), joined_at = m.JoinedAt, last_login_at = m.LastLoginAt }),
            invitations = detail.Invitations.Select(i => new
            {
                invitation_id = i.InvitationId, email = i.Email, role = i.Role.ToString(), created_at = i.CreatedAt,
                expires_at = i.ExpiresAt, accepted_at = i.AcceptedAt, revoked_at = i.RevokedAt
            })
        });
    }

    [HttpPatch("organizations/{tenantId}")]
    public async Task<IActionResult> Update(string tenantId, [FromBody] UpdateOrganizationBody body, CancellationToken ct)
    {
        if (await admin.GetAsync(tenantId, ct) is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(body.Name)) await admin.RenameAsync(tenantId, body.Name, Operator, ct);
        return NoContent();
    }

    [HttpPost("organizations/{tenantId}/suspend")]
    public async Task<IActionResult> Suspend(string tenantId, [FromBody] SuspendBody? body, CancellationToken ct)
    {
        var result = await admin.SuspendAsync(tenantId, body?.Reason, Operator, ct);
        return result.Success ? NoContent() : result.Error == "No such organization." ? NotFound() : BadRequest(new { error = result.Error });
    }

    [HttpPost("organizations/{tenantId}/reactivate")]
    public async Task<IActionResult> Reactivate(string tenantId, CancellationToken ct) =>
        await admin.ReactivateAsync(tenantId, Operator, ct) ? NoContent() : NotFound();

    [HttpPost("organizations/{tenantId}/members")]
    public async Task<IActionResult> AddMember(string tenantId, [FromBody] AddMemberBody body, CancellationToken ct)
    {
        var role = TenantRole.Member;
        if (body.Role is not null && !Enum.TryParse(body.Role, ignoreCase: true, out role)) return BadRequest(new { error = "role must be Viewer, Member, Admin or Owner" });
        var result = await admin.AddMemberAsync(tenantId, body.Email, role, Operator, ct);
        return result.Success ? Ok(ToJson(result.Value!)) : BadRequest(new { error = result.Error });
    }

    [HttpPut("organizations/{tenantId}/members/{userId}/role")]
    public async Task<IActionResult> SetRole(string tenantId, string userId, [FromBody] RoleBody body, CancellationToken ct)
    {
        if (!Enum.TryParse<TenantRole>(body.Role, ignoreCase: true, out var role)) return BadRequest(new { error = "role must be Viewer, Member, Admin or Owner" });
        var result = await admin.SetMemberRoleAsync(tenantId, userId, role, Operator, ct);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpDelete("organizations/{tenantId}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(string tenantId, string userId, CancellationToken ct)
    {
        var result = await admin.RemoveMemberAsync(tenantId, userId, Operator, ct);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpDelete("organizations/{tenantId}/invitations/{invitationId}")]
    public async Task<IActionResult> RevokeInvitation(string tenantId, string invitationId, CancellationToken ct) =>
        await admin.RevokeInvitationAsync(tenantId, invitationId, Operator, ct) ? NoContent() : NotFound();

    // ---- People --------------------------------------------------------------------------------

    /// <summary>For someone locked out by two-factor sign-in (lost phone and recovery codes): turns
    /// it off and signs out every browser, so they can sign in with their password again.</summary>
    [HttpPost("users/two-factor/reset")]
    public async Task<IActionResult> ResetTwoFactor([FromBody] EmailBody body, CancellationToken ct) =>
        await admin.ResetTwoFactorAsync(body.Email, Operator, ct) switch
        {
            Infrastructure.Identity.TwoFactorReset.Done => NoContent(),
            Infrastructure.Identity.TwoFactorReset.AlreadyOff => Conflict(new { error = "Two-factor sign-in is already off for that account." }),
            _ => NotFound(new { error = "No account has that email." })
        };

    private static object ToJson(OrganizationSummary o) => new
    {
        tenant_id = o.TenantId, name = o.Name, status = o.Status, created_at = o.CreatedAt, members = o.Members,
        pending_invitations = o.PendingInvitations, owner_email = o.OwnerEmail, last_active_at = o.LastActiveAt,
        suspended_at = o.SuspendedAt, suspended_reason = o.SuspendedReason
    };

    private static object ToJson(AddedPerson p) => new
    {
        email = p.Email, role = p.Role.ToString(), joined = p.Joined, invitation_id = p.InvitationId, invite_url = p.InviteUrl, email_sent = p.EmailSent
    };

}
