using AgentRuntime.Api.Platform;
using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AgentRuntime.Api.Controllers;

/// <summary>Accounts and sessions (docs/platform.md). The session lives in an HttpOnly cookie that
/// scripts can't read; API clients use API keys instead.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(IdentityService identity, TwoFactorService twoFactor, IOptions<AuthOptions> authOptions) : ControllerBase
{
    public sealed record SignUpBody(string Email, string Password, string? Name, string? Organization, string? Invitation, string? SetupToken = null);
    public sealed record SignInBody(string Email, string Password);
    public sealed record SecondFactorBody(string Challenge, string Code);
    public sealed record PasswordOnlyBody(string Password);
    public sealed record CodeBody(string Code);
    public sealed record PasswordAndCodeBody(string Password, string Code);
    public sealed record SwitchBody(string TenantId);
    public sealed record PasswordBody(string CurrentPassword, string NewPassword);
    public sealed record AcceptBody(string Invitation);
    public sealed record VerifyBody(string Token, string? Password);
    public sealed record ResendBody(string Id);

    private AuthOptions Auth => authOptions.Value;

    /// <summary>The caller's address (the visitor's own behind the proxy, with ForwardedHeaders on).</summary>
    private string? ClientAddress => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost("signup")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> SignUp([FromBody] SignUpBody body, CancellationToken ct)
    {
        if (Auth.Disabled) return BadRequest(new { error = "Accounts are disabled on this server (Auth:Mode=disabled)." });
        try
        {
            var result = await identity.RequestSignUpAsync(body.Email, body.Password, body.Name, body.Organization, body.Invitation, ct, body.SetupToken, ClientAddress);
            var signedIn = result.Value?.SignedIn;
            if (!result.Success) return BadRequest(new { error = result.Error });
            // An open sign-up confirms its email first: no account or session until the link is opened.
            if (result.Value!.VerificationRequired)
            {
                return Ok(new { verification_required = true, email = result.Value.VerificationSentTo, resend_id = result.Value.PendingId, expires_in_hours = (int)IdentityService.VerificationLinkLifetime.TotalHours });
            }

            SetCookie(signedIn!.SessionToken!);
            return Ok(new { user_id = signedIn.User.UserId, tenant_id = signedIn.TenantId });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            return BadRequest(new { error = "An account with this email already exists. Sign in instead." });
        }
    }

    /// <summary>Opens the link from the confirmation email: creates the account and signs it in.</summary>
    [HttpPost("signup/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifySignUp([FromBody] VerifyBody body, CancellationToken ct)
    {
        if (Auth.Disabled) return BadRequest(new { error = "Accounts are disabled on this server (Auth:Mode=disabled)." });
        try
        {
            var result = await identity.CompleteSignUpAsync(body.Token, body.Password ?? string.Empty, ct);
            // 422: the password was wrong and the link still works; 400: the link doesn't.
            if (result.Error == IdentityService.WrongConfirmationPassword) return UnprocessableEntity(new { error = result.Error });
            if (!result.Success) return BadRequest(new { error = result.Error });
            SetCookie(result.Value!.SessionToken!);
            return Ok(new { user_id = result.Value.User.UserId, tenant_id = result.Value.TenantId });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            return BadRequest(new { error = "An account with this email already exists. Sign in instead." });
        }
    }

    /// <summary>Emails a new confirmation link for the sign-up whose <c>resend_id</c> the sign-up
    /// returned. Answers the same whether or not it's still waiting, unless the email can't be sent (502).</summary>
    [HttpPost("signup/resend")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendBody body, CancellationToken ct)
    {
        if (Auth.Disabled) return BadRequest(new { error = "Accounts are disabled on this server (Auth:Mode=disabled)." });
        if (!await identity.ResendSignUpVerificationAsync(body.Id, ct))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "We couldn't send the email just now. The link we sent earlier still works; try again in a few minutes." });
        }

        return Ok(new { sent = true });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] SignInBody body, CancellationToken ct)
    {
        if (Auth.Disabled) return BadRequest(new { error = "Accounts are disabled on this server (Auth:Mode=disabled)." });
        var result = await identity.SignInAsync(body.Email, body.Password, ct, ClientAddress);
        if (!result.Success) return Unauthorized(new { error = result.Error });
        // Two-factor sign-in: no session until the code checks out (POST login/two-factor).
        if (result.Value!.NeedsSecondFactor)
        {
            return Ok(new { two_factor_required = true, challenge = result.Value.TwoFactorChallenge, expires_in_seconds = (int)IdentityService.LoginChallengeLifetime.TotalSeconds });
        }

        SetCookie(result.Value.SessionToken!);
        return Ok(new { user_id = result.Value.User.UserId, tenant_id = result.Value.TenantId });
    }

    /// <summary>The second step of signing in with two-factor sign-in on: the challenge from
    /// <c>login</c> and a code from the authenticator app, or a recovery code.</summary>
    [HttpPost("login/two-factor")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> LoginSecondFactor([FromBody] SecondFactorBody body, CancellationToken ct)
    {
        if (Auth.Disabled) return BadRequest(new { error = "Accounts are disabled on this server (Auth:Mode=disabled)." });
        var result = await twoFactor.CompleteSignInAsync(body.Challenge, body.Code, ct);
        if (!result.Success) return Unauthorized(new { error = result.Error });
        SetCookie(result.Value!.SessionToken!);
        return Ok(new { user_id = result.Value.User.UserId, tenant_id = result.Value.TenantId });
    }

    /// <summary>Whether the signed-in person has two-factor sign-in on.</summary>
    [HttpGet("two-factor")]
    public async Task<IActionResult> TwoFactorStatus(CancellationToken ct)
    {
        if (SignedInUser() is not { } userId) return BadRequest(new { error = "Only a signed-in person has two-factor sign-in." });
        var status = await twoFactor.StatusAsync(userId, ct);
        return Ok(new { enabled = status.Enabled, enabled_at = status.EnabledAt, recovery_codes_left = status.RecoveryCodesLeft });
    }

    /// <summary>Starts setting up: the secret and QR code for the authenticator app.</summary>
    [HttpPost("two-factor/setup")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> TwoFactorSetup([FromBody] PasswordOnlyBody body, CancellationToken ct)
    {
        if (SignedInUser() is not { } userId) return BadRequest(new { error = "Only a signed-in person has two-factor sign-in." });
        var result = await twoFactor.BeginSetupAsync(userId, body.Password, ct);
        return result.Success
            ? Ok(new { secret = result.Value!.Secret, otpauth_uri = result.Value.Uri, qr_svg = result.Value.QrSvg })
            : BadRequest(new { error = result.Error });
    }

    /// <summary>Confirms the setup with the app's first code. Returns the recovery codes, once.</summary>
    [HttpPost("two-factor/enable")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> TwoFactorEnable([FromBody] CodeBody body, CancellationToken ct)
    {
        var caller = HttpContext.Caller();
        if (SignedInUser() is not { } userId || caller.SessionHash is null) return BadRequest(new { error = "Only a signed-in person has two-factor sign-in." });
        var result = await twoFactor.EnableAsync(userId, body.Code, caller.SessionHash, ct);
        return result.Success ? Ok(new { recovery_codes = result.Value }) : BadRequest(new { error = result.Error });
    }

    [HttpPost("two-factor/disable")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> TwoFactorDisable([FromBody] PasswordAndCodeBody body, CancellationToken ct)
    {
        if (SignedInUser() is not { } userId) return BadRequest(new { error = "Only a signed-in person has two-factor sign-in." });
        var result = await twoFactor.DisableAsync(userId, body.Password, body.Code, ct);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>New recovery codes, replacing the old ones.</summary>
    [HttpPost("two-factor/recovery-codes")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> TwoFactorRecoveryCodes([FromBody] PasswordAndCodeBody body, CancellationToken ct)
    {
        if (SignedInUser() is not { } userId) return BadRequest(new { error = "Only a signed-in person has two-factor sign-in." });
        var result = await twoFactor.RegenerateRecoveryCodesAsync(userId, body.Password, body.Code, ct);
        return result.Success ? Ok(new { recovery_codes = result.Value }) : BadRequest(new { error = result.Error });
    }

    /// <summary>The signed-in person behind a browser session; null on auth-less servers.</summary>
    private string? SignedInUser()
    {
        var caller = HttpContext.Caller();
        return Auth.Disabled || caller.SessionHash is null ? null : caller.UserId;
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (SessionAuthenticationHandler.CallerOf(HttpContext) is { SessionHash: { } hash }) await identity.SignOutAsync(hash, ct);
        Response.Cookies.Delete(Auth.CookieName, CookieOptions());
        return NoContent();
    }

    /// <summary>Who's signed in, their organizations, and how this server is set up. Anonymous
    /// callers get the setup only, which is what the sign-in page needs.</summary>
    [HttpGet("me")]
    [AllowAnonymous]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var server = new
        {
            auth_mode = Auth.Disabled ? "disabled" : "accounts",
            signup_allowed = Auth.AllowSignup,
            // Open sign-ups get an email to confirm the address before the account exists.
            email_verification = identity.VerifiesEmail,
            // No accounts yet: the sign-up form asks for the setup token, even with sign-up off.
            setup_required = await identity.SetupPendingAsync(ct)
        };
        if (SessionAuthenticationHandler.CallerOf(HttpContext) is not { } caller)
        {
            return Ok(new { authenticated = false, server });
        }

        var memberships = caller.UserId is { } userId && !Auth.Disabled
            ? await identity.MembershipsAsync(userId, ct)
            : [new MembershipView(caller.TenantId, (await identity.GetTenantAsync(caller.TenantId, ct))?.Name ?? "Default", caller.Role)];
        return Ok(new
        {
            authenticated = true,
            server,
            user = new
            {
                user_id = caller.UserId,
                email = caller.Email,
                platform_admin = caller.PlatformAdmin,
                two_factor_enabled = caller.UserId is { } id && caller.SessionHash is not null && (await twoFactor.StatusAsync(id, ct)).Enabled
            },
            via = Auth.Disabled ? "disabled" : "session",
            tenant_id = caller.TenantId,
            role = caller.Role.ToString(),
            organizations = memberships.Select(m => new { tenant_id = m.TenantId, name = m.TenantName, role = m.Role.ToString(), suspended = m.Suspended })
        });
    }

    [HttpPost("switch")]
    public async Task<IActionResult> Switch([FromBody] SwitchBody body, CancellationToken ct)
    {
        var caller = HttpContext.Caller();
        if (caller.SessionHash is null || caller.UserId is null) return BadRequest(new { error = "Only a signed-in browser can switch organizations." });
        return await identity.SwitchTenantAsync(caller.SessionHash, caller.UserId, body.TenantId, ct) ? NoContent() : NotFound();
    }

    [HttpPost("password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword([FromBody] PasswordBody body, CancellationToken ct)
    {
        var caller = HttpContext.Caller();
        if (caller.UserId is null || caller.SessionHash is null) return BadRequest(new { error = "Sign in to change your password." });
        var result = await identity.ChangePasswordAsync(caller.UserId, body.CurrentPassword, body.NewPassword, caller.SessionHash, ct);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>What an invitation link is for (for the sign-up page).</summary>
    [HttpGet("invitations/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> PeekInvitation(string token, CancellationToken ct) =>
        await identity.PeekInvitationAsync(token, ct) is { } i
            ? Ok(new { organization = i.TenantName, email = i.Email, role = i.Role.ToString() })
            : NotFound(new { error = "This invitation is invalid or has expired." });

    /// <summary>A signed-in user joining the organization they were invited to.</summary>
    [HttpPost("invitations/accept")]
    public async Task<IActionResult> Accept([FromBody] AcceptBody body, CancellationToken ct)
    {
        var caller = HttpContext.Caller();
        if (caller.UserId is null || caller.SessionHash is null) return BadRequest(new { error = "Sign in to accept an invitation." });
        var result = await identity.AcceptInvitationAsync(caller.UserId, body.Invitation, ct);
        if (!result.Success) return BadRequest(new { error = result.Error });
        await identity.SwitchTenantAsync(caller.SessionHash, caller.UserId, result.Value!, ct);
        return Ok(new { tenant_id = result.Value });
    }

    private void SetCookie(string token) =>
        Response.Cookies.Append(Auth.CookieName, token, CookieOptions(DateTimeOffset.UtcNow.Add(Auth.SessionLifetime)));

    private CookieOptions CookieOptions(DateTimeOffset? expires = null) => SessionCookie.Options(Request, Auth, expires);
}

/// <summary>The signed-in person deleting their own account (docs/platform.md#deleting-your-account).</summary>
[ApiController]
[Route("api/account")]
public sealed class AccountController(IdentityService identity, AccountDeletionService deletion, IOptions<AuthOptions> authOptions) : ControllerBase
{
    public sealed record DeleteBody(string Password);

    /// <summary>What deleting the account would do: which organizations it leaves, which close with
    /// it, and which block it.</summary>
    [HttpGet("deletion")]
    public async Task<IActionResult> Preview(CancellationToken ct)
    {
        var caller = HttpContext.Caller();
        if (caller.UserId is null || caller.SessionHash is null) return BadRequest(new { error = "Sign in to delete your account." });
        if (await identity.PlanAccountDeletionAsync(caller.UserId, ct) is not { } plan) return NotFound();
        static object Org(MembershipView m) => new { tenant_id = m.TenantId, name = m.TenantName, role = m.Role.ToString() };
        return Ok(new
        {
            allowed = plan.Allowed,
            is_operator = plan.IsOperator,
            leaves = plan.Leaves.Select(Org),
            closes = plan.Closes.Select(Org),
            blocks = plan.Blocks.Select(Org)
        });
    }

    [HttpPost("delete")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Delete([FromBody] DeleteBody body, CancellationToken ct)
    {
        var caller = HttpContext.Caller();
        if (caller.UserId is null || caller.SessionHash is null) return BadRequest(new { error = "Sign in to delete your account." });
        var outcome = await deletion.DeleteAsync(caller.UserId, body.Password ?? string.Empty, ct);
        if (!outcome.Success) return BadRequest(new { error = outcome.Error });
        Response.Cookies.Delete(authOptions.Value.CookieName, SessionCookie.Options(Request, authOptions.Value));
        return Ok(new { organizations_left = outcome.OrganizationsLeft, organizations_closed = outcome.OrganizationsClosed });
    }
}

/// <summary>The caller's organization: its name, members and invitations.</summary>
[ApiController]
[Route("api/organization")]
public sealed class OrganizationController(IdentityService identity) : ControllerBase
{
    public sealed record RenameBody(string Name);
    public sealed record InviteBody(string Email, string Role);
    public sealed record RoleBody(string Role);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var caller = HttpContext.Caller();
        var tenant = await identity.GetTenantAsync(caller.TenantId, ct);
        return Ok(new
        {
            tenant_id = caller.TenantId,
            name = tenant?.Name ?? "Default",
            created_at = tenant?.CreatedAt,
            your_role = caller.Role.ToString(),
            members = (await identity.MembersAsync(caller.TenantId, ct)).Count
        });
    }

    [HttpPatch]
    [Authorize(Policies.Admin)]
    public async Task<IActionResult> Rename([FromBody] RenameBody body, CancellationToken ct)
    {
        await identity.RenameTenantAsync(HttpContext.Caller().TenantId, body.Name, ct);
        return NoContent();
    }

    [HttpGet("members")]
    public async Task<IActionResult> Members(CancellationToken ct) =>
        Ok((await identity.MembersAsync(HttpContext.Caller().TenantId, ct)).Select(m => new
        {
            user_id = m.UserId, email = m.Email, name = m.Name, role = m.Role.ToString(), joined_at = m.JoinedAt
        }));

    [HttpPut("members/{userId}/role")]
    [Authorize(Policies.Admin)]
    public async Task<IActionResult> SetRole(string userId, [FromBody] RoleBody body, CancellationToken ct)
    {
        if (!Enum.TryParse<TenantRole>(body.Role, ignoreCase: true, out var role)) return BadRequest(new { error = "role must be Viewer, Member, Admin or Owner" });
        var result = await identity.SetRoleAsync(HttpContext.Caller(), userId, role, ct);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpDelete("members/{userId}")]
    public async Task<IActionResult> Remove(string userId, CancellationToken ct)
    {
        var caller = HttpContext.Caller();
        // Anyone may leave; removing someone else takes an admin.
        if (caller.UserId != userId && caller.Role < TenantRole.Admin) return StatusCode(403, new { error = "Only admins can remove members." });
        var result = await identity.RemoveMemberAsync(caller, userId, ct);
        return result.Success ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpGet("invitations")]
    [Authorize(Policies.Admin)]
    public async Task<IActionResult> Invitations(CancellationToken ct) =>
        Ok((await identity.InvitationsAsync(HttpContext.Caller().TenantId, ct)).Select(i => new
        {
            invitation_id = i.InvitationId, email = i.Email, role = i.Role.ToString(), created_at = i.CreatedAt,
            expires_at = i.ExpiresAt, accepted_at = i.AcceptedAt, revoked_at = i.RevokedAt
        }));

    /// <summary>Returns the invitation token once; the dashboard turns it into a link to share.</summary>
    [HttpPost("invitations")]
    [Authorize(Policies.Admin)]
    public async Task<IActionResult> Invite([FromBody] InviteBody body, CancellationToken ct)
    {
        if (!Enum.TryParse<TenantRole>(body.Role, ignoreCase: true, out var role)) return BadRequest(new { error = "role must be Viewer, Member, Admin or Owner" });
        var result = await identity.InviteAsync(HttpContext.Caller(), body.Email, role, ct);
        if (!result.Success) return BadRequest(new { error = result.Error });
        var (invitation, token) = result.Value;
        return Ok(new { invitation_id = invitation.InvitationId, email = invitation.Email, role = invitation.Role.ToString(), expires_at = invitation.ExpiresAt, token });
    }

    [HttpDelete("invitations/{invitationId}")]
    [Authorize(Policies.Admin)]
    public async Task<IActionResult> Revoke(string invitationId, CancellationToken ct) =>
        await identity.RevokeInvitationAsync(HttpContext.Caller().TenantId, invitationId, ct) ? NoContent() : NotFound();
}
