using System.Text.Json;
using AgentRuntime.Infrastructure.Persistence;
using AgentRuntime.Safety;
using AgentRuntime.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentRuntime.Infrastructure.Identity;

public sealed record OrganizationSummary(string TenantId, string Name, string Status, DateTimeOffset CreatedAt,
    int Members, int PendingInvitations, string? OwnerEmail, DateTimeOffset? LastActiveAt,
    DateTimeOffset? SuspendedAt, string? SuspendedReason);

public sealed record OrganizationMemberView(string UserId, string Email, string Name, TenantRole Role, DateTimeOffset JoinedAt, DateTimeOffset? LastLoginAt);

public sealed record OrganizationDetail(OrganizationSummary Summary, IReadOnlyList<OrganizationMemberView> Members, IReadOnlyList<InvitationView> Invitations);

/// <summary>What adding someone produced: a membership straight away (they already had an
/// account), or an invitation to accept, with its one-time link.</summary>
public sealed record AddedPerson(string Email, TenantRole Role, bool Joined, string? InvitationId, string? InviteUrl, bool EmailSent);

/// <summary>
/// The platform operators' view across every organization (docs/platform.md#platform-administration):
/// list, create, suspend and reactivate organizations, and manage anyone's membership. Callers must
/// already be checked as operators. Every change is written to the "platform" audit scope.
/// </summary>
public sealed class PlatformAdminService(
    IDbContextFactory<AgentDbContext> dbFactory,
    IdentityService identity,
    TwoFactorService twoFactor,
    PlatformMailer mailer,
    IAuditLog audit,
    IOptions<DashboardOptions> links,
    ILogger<PlatformAdminService> logger)
{
    public const string AuditScope = "platform";
    private const int MaxListed = 500;

    public string InviteUrl(string token) => $"{links.Value.BaseUrl.TrimEnd('/')}/login?invite={Uri.EscapeDataString(token)}";

    public async Task<IReadOnlyList<OrganizationSummary>> ListAsync(string? search, string? status, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Tenants.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(t => t.Status == status);
        if (search?.Trim() is { Length: > 0 } term)
        {
            var like = $"%{term.Replace("%", "\\%").Replace("_", "\\_")}%";
            // Matches the organization's name or id, or any member's email.
            var byMember = db.Memberships.Join(db.Users, m => m.UserId, u => u.UserId, (m, u) => new { m.TenantId, u.Email })
                .Where(x => EF.Functions.ILike(x.Email, like)).Select(x => x.TenantId);
            query = query.Where(t => EF.Functions.ILike(t.Name, like) || EF.Functions.ILike(t.TenantId, like) || byMember.Contains(t.TenantId));
        }

        var tenants = await query.OrderByDescending(t => t.CreatedAt).Take(MaxListed).ToListAsync(ct);
        return await SummarizeAsync(db, tenants, ct);
    }

    /// <summary>Totals across the whole server (the list itself is capped).</summary>
    public async Task<(int Organizations, int Suspended, int Members)> CountsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.Tenants.CountAsync(ct), await db.Tenants.CountAsync(t => t.Status == TenantStatuses.Suspended, ct),
            await db.Memberships.CountAsync(ct));
    }

    public async Task<OrganizationDetail?> GetAsync(string tenantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (tenant is null) return null;
        var summary = (await SummarizeAsync(db, [tenant], ct))[0];
        var members = await db.Memberships.Where(m => m.TenantId == tenantId)
            .Join(db.Users, m => m.UserId, u => u.UserId, (m, u) => new { m, u })
            .OrderBy(x => x.m.CreatedAt)
            .Select(x => new { x.u.UserId, x.u.Email, x.u.Name, x.m.Role, x.m.CreatedAt, x.u.LastLoginAt })
            .ToListAsync(ct);
        return new OrganizationDetail(summary,
            members.Select(m => new OrganizationMemberView(m.UserId, m.Email, m.Name, IdentityService.ParseRole(m.Role), m.CreatedAt, m.LastLoginAt)).ToList(),
            await identity.InvitationsAsync(tenantId, ct));
    }

    private static async Task<List<OrganizationSummary>> SummarizeAsync(AgentDbContext db, List<TenantRecord> tenants, CancellationToken ct)
    {
        var ids = tenants.Select(t => t.TenantId).ToList();
        var now = DateTimeOffset.UtcNow;
        var people = await db.Memberships.Where(m => ids.Contains(m.TenantId))
            .Join(db.Users, m => m.UserId, u => u.UserId, (m, u) => new { m.TenantId, m.Role, m.CreatedAt, u.Email, u.LastLoginAt })
            .ToListAsync(ct);
        var invitations = await db.Invitations.Where(i => ids.Contains(i.TenantId) && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .GroupBy(i => i.TenantId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return tenants.Select(t =>
        {
            var members = people.Where(p => p.TenantId == t.TenantId).ToList();
            var owner = members.Where(p => p.Role == nameof(TenantRole.Owner)).OrderBy(p => p.CreatedAt).FirstOrDefault();
            return new OrganizationSummary(t.TenantId, t.Name, t.Status, t.CreatedAt, members.Count,
                invitations.GetValueOrDefault(t.TenantId), owner?.Email,
                members.Max(p => p.LastLoginAt), t.SuspendedAt, t.SuspendedReason);
        }).ToList();
    }

    /// <summary>Creates an organization and gives it its first owner: added straight away if they
    /// already have an account, otherwise invited (and emailed the link when mail is set up).</summary>
    public async Task<IdentityResult<(string TenantId, AddedPerson Owner)>> CreateAsync(string name, string ownerEmail, string operatorEmail,
        CancellationToken ct)
    {
        var clipped = name?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 100)] : null;
        if (clipped is null) return IdentityResult<(string, AddedPerson)>.Fail("Give the organization a name.");
        var email = IdentityService.NormalizeEmail(ownerEmail ?? string.Empty);
        if (!email.Contains('@')) return IdentityResult<(string, AddedPerson)>.Fail("Enter the owner's email address.");

        var tenantId = TenantIds.New();
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.Tenants.Add(new TenantRecord { TenantId = tenantId, Name = clipped, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        var owner = await AddPersonAsync(tenantId, email, TenantRole.Owner, operatorEmail, ct);
        if (!owner.Success)
        {
            // Nothing references the new organization yet, so it can go again.
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Tenants.Where(t => t.TenantId == tenantId).ExecuteDeleteAsync(ct);
            return IdentityResult<(string, AddedPerson)>.Fail(owner.Error!);
        }

        await AuditAsync(operatorEmail, "organization.created", tenantId, $"Created organization \"{clipped}\" with owner {email}",
            new { tenant_id = tenantId, name = clipped, owner = email }, ct);
        return IdentityResult<(string, AddedPerson)>.Ok((tenantId, owner.Value!));
    }

    /// <summary>Adds someone to an organization. An existing account joins straight away (and is
    /// told by email); anyone else gets an invitation, since only they should choose their password.</summary>
    public async Task<IdentityResult<AddedPerson>> AddMemberAsync(string tenantId, string email, TenantRole role, string operatorEmail, CancellationToken ct)
    {
        var result = await AddPersonAsync(tenantId, email, role, operatorEmail, ct);
        if (result.Success)
        {
            await AuditAsync(operatorEmail, result.Value!.Joined ? "member.added" : "member.invited", tenantId,
                $"{(result.Value.Joined ? "Added" : "Invited")} {result.Value.Email} as {role}", new { tenant_id = tenantId, email = result.Value.Email, role = role.ToString() }, ct);
        }

        return result;
    }

    private async Task<IdentityResult<AddedPerson>> AddPersonAsync(string tenantId, string email, TenantRole role, string operatorEmail, CancellationToken ct)
    {
        email = IdentityService.NormalizeEmail(email ?? string.Empty);
        string orgName;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var tenant = await db.Tenants.FindAsync([tenantId], ct);
            if (tenant is null) return IdentityResult<AddedPerson>.Fail("No such organization.");
            orgName = tenant.Name;
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (user is not null)
            {
                if (await db.Memberships.FindAsync([tenantId, user.UserId], ct) is not null) return IdentityResult<AddedPerson>.Fail("They're already a member.");
                db.Memberships.Add(new MembershipRecord { TenantId = tenantId, UserId = user.UserId, Role = role.ToString(), CreatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(ct);
                var told = await mailer.SendAsync(email, new PlatformEmail
                {
                    Subject = $"You've been added to {orgName}",
                    Greeting = string.IsNullOrWhiteSpace(user.Name) ? "Hi," : $"Hi {user.Name},",
                    Paragraphs = [$"You now have {role} access to {orgName}. Sign in as usual and switch to it from the organization menu."],
                    Action = new EmailAction("Sign in", $"{links.Value.BaseUrl.TrimEnd('/')}/login"),
                    Footnote = $"You're getting this because an operator added {email} to {orgName}."
                }, ct);
                return IdentityResult<AddedPerson>.Ok(new AddedPerson(email, role, Joined: true, null, null, told));
            }
        }

        var invited = await identity.InviteAsOperatorAsync(tenantId, email, role, operatorEmail, ct);
        if (!invited.Success) return IdentityResult<AddedPerson>.Fail(invited.Error!);
        var (invitation, token) = invited.Value;
        var url = InviteUrl(token);
        var sent = await mailer.SendAsync(email, InvitationEmail(orgName, role, url, invitation.ExpiresAt), ct);
        return IdentityResult<AddedPerson>.Ok(new AddedPerson(email, role, Joined: false, invitation.InvitationId, url, sent));
    }

    public static PlatformEmail InvitationEmail(string orgName, TenantRole role, string url, DateTimeOffset expiresAt) => new()
    {
        Subject = $"Your invitation to {orgName}",
        Heading = $"You're invited to {orgName}",
        Greeting = "Hi,",
        Paragraphs = [$"You've been given {role} access to {orgName} on {PlatformEmailTemplate.ProductName}. Accept the invitation to create your account and set your password."],
        Action = new EmailAction("Accept invitation", url),
        Footnote = $"The link works once, only for this email address, and expires on {expiresAt.UtcDateTime.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)} (UTC). If you weren't expecting it, you can ignore this email."
    };

    public async Task<IdentityResult<bool>> SetMemberRoleAsync(string tenantId, string userId, TenantRole role, string operatorEmail, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var membership = await db.Memberships.FindAsync([tenantId, userId], ct);
        if (membership is null) return IdentityResult<bool>.Fail("No such member.");
        if (membership.Role == nameof(TenantRole.Owner) && role != TenantRole.Owner && await OwnerCountAsync(db, tenantId, ct) <= 1)
        {
            return IdentityResult<bool>.Fail("An organization needs at least one owner. Make someone else an owner first.");
        }

        var before = membership.Role;
        membership.Role = role.ToString();
        await db.SaveChangesAsync(ct);
        await AuditAsync(operatorEmail, "member.role_changed", tenantId, $"Changed {userId} from {before} to {role}", new { tenant_id = tenantId, user_id = userId, from = before, to = role.ToString() }, ct);
        return IdentityResult<bool>.Ok(true);
    }

    public async Task<IdentityResult<bool>> RemoveMemberAsync(string tenantId, string userId, string operatorEmail, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var membership = await db.Memberships.FindAsync([tenantId, userId], ct);
        if (membership is null) return IdentityResult<bool>.Fail("No such member.");
        if (membership.Role == nameof(TenantRole.Owner) && await OwnerCountAsync(db, tenantId, ct) <= 1)
        {
            return IdentityResult<bool>.Fail("An organization needs at least one owner. Make someone else an owner first.");
        }

        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);
        await db.Sessions.Where(s => s.UserId == userId && s.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await AuditAsync(operatorEmail, "member.removed", tenantId, $"Removed {userId}", new { tenant_id = tenantId, user_id = userId }, ct);
        return IdentityResult<bool>.Ok(true);
    }

    public async Task<bool> RevokeInvitationAsync(string tenantId, string invitationId, string operatorEmail, CancellationToken ct)
    {
        if (!await identity.RevokeInvitationAsync(tenantId, invitationId, ct)) return false;
        await AuditAsync(operatorEmail, "invitation.revoked", tenantId, $"Revoked invitation {invitationId}", new { tenant_id = tenantId, invitation_id = invitationId }, ct);
        return true;
    }

    public async Task<bool> RenameAsync(string tenantId, string name, string operatorEmail, CancellationToken ct)
    {
        if (await identity.GetTenantAsync(tenantId, ct) is null || string.IsNullOrWhiteSpace(name)) return false;
        await identity.RenameTenantAsync(tenantId, name, ct);
        await AuditAsync(operatorEmail, "organization.renamed", tenantId, $"Renamed to \"{name.Trim()}\"", new { tenant_id = tenantId, name = name.Trim() }, ct);
        return true;
    }

    /// <summary>Turns off two-factor sign-in for someone who lost their phone and recovery codes,
    /// and signs out their browsers. False when there's no account with that email.</summary>
    public async Task<TwoFactorReset> ResetTwoFactorAsync(string email, string operatorEmail, CancellationToken ct)
    {
        var result = await twoFactor.ResetAsync(email, ct);
        if (result != TwoFactorReset.Done) return result;
        var normalized = IdentityService.NormalizeEmail(email);
        await AuditAsync(operatorEmail, "user.two_factor_reset", normalized, $"Turned off two-factor sign-in for {normalized}", new { email = normalized }, ct);
        return result;
    }

    /// <summary>
    /// Suspends an organization without deleting anything: its members are signed out of it and
    /// can't sign back in to it, and its invitations stop working. Reactivating undoes it.
    /// An organization an operator belongs to can't be suspended, so nobody locks themselves out.
    /// </summary>
    public async Task<IdentityResult<bool>> SuspendAsync(string tenantId, string? reason, string operatorEmail, CancellationToken ct)
    {
        var why = reason?.Trim() is { Length: > 0 } r ? r[..Math.Min(r.Length, 300)] : "suspended by an operator";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return IdentityResult<bool>.Fail("No such organization.");
        if (tenant.Status == TenantStatuses.Suspended) return IdentityResult<bool>.Ok(true);

        var memberEmails = await db.Memberships.Where(m => m.TenantId == tenantId)
            .Join(db.Users, m => m.UserId, u => u.UserId, (m, u) => u.Email).ToListAsync(ct);
        if (memberEmails.Any(identity.IsPlatformAdmin))
        {
            return IdentityResult<bool>.Fail("A platform operator belongs to this organization; suspending it would lock them out.");
        }

        tenant.Status = TenantStatuses.Suspended;
        tenant.SuspendedAt = DateTimeOffset.UtcNow;
        tenant.SuspendedReason = why;
        await db.SaveChangesAsync(ct);
        await db.Sessions.Where(s => s.TenantId == tenantId).ExecuteDeleteAsync(ct);
        logger.LogWarning("Organization {TenantId} suspended by {Operator}: {Reason}", tenantId, operatorEmail, why);
        await AuditAsync(operatorEmail, "organization.suspended", tenantId, $"Suspended \"{tenant.Name}\": {why}", new { tenant_id = tenantId, reason = why }, ct);
        return IdentityResult<bool>.Ok(true);
    }

    public async Task<bool> ReactivateAsync(string tenantId, string operatorEmail, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null) return false;
        if (tenant.Status != TenantStatuses.Active)
        {
            tenant.Status = TenantStatuses.Active;
            tenant.SuspendedAt = null;
            tenant.SuspendedReason = null;
            await db.SaveChangesAsync(ct);
            await AuditAsync(operatorEmail, "organization.reactivated", tenantId, $"Reactivated \"{tenant.Name}\"", new { tenant_id = tenantId }, ct);
        }

        return true;
    }

    private static Task<int> OwnerCountAsync(AgentDbContext db, string tenantId, CancellationToken ct) =>
        db.Memberships.CountAsync(m => m.TenantId == tenantId && m.Role == nameof(TenantRole.Owner), ct);

    public async Task<IReadOnlyList<AuditEntry>> RecentActivityAsync(int limit, CancellationToken ct) =>
        await audit.QueryAsync(new AuditQuery { Scope = AuditScope, Limit = Math.Clamp(limit, 1, 200) }, ct);

    internal async Task AuditAsync(string operatorEmail, string action, string target, string summary, object detail, CancellationToken ct)
    {
        try
        {
            await audit.AppendAsync(new AuditEntry
            {
                Scope = AuditScope,
                Key = $"{action}:{target}:{Guid.NewGuid():n}",
                ActorType = "user",
                ActorId = operatorEmail,
                ActorName = operatorEmail,
                Action = action,
                Target = target,
                Summary = summary,
                DetailJson = JsonSerializer.Serialize(detail)
            }, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // The change itself is done; a missing audit line is logged rather than undoing it.
            logger.LogError(ex, "Couldn't write the platform audit entry for {Action} on {Target}", action, target);
        }
    }
}
