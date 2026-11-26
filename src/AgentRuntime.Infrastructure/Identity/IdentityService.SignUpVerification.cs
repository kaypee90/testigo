using AgentRuntime.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentRuntime.Infrastructure.Identity;

/// <summary>What became of a sign-up request: an account straight away, or a link emailed to confirm
/// the address first, with an id the same browser can use to have it sent again.</summary>
public sealed record SignUpRequested(SignedIn? SignedIn, string? VerificationSentTo, string? PendingId = null)
{
    public bool VerificationRequired => VerificationSentTo is not null;
}

/// <summary>
/// Confirming a new account's email (docs/platform.md#email-verification). The account isn't created
/// until the link emailed to the address is opened and the password chosen at sign-up is entered with
/// it, so nobody can sign up with an address that isn't theirs, and an address nobody confirmed never
/// blocks its owner from signing up. Invitations confirm too: an invitation link can be copied and
/// passed on, so it doesn't prove who owns the address it names.
/// </summary>
public sealed partial class IdentityService
{
    /// <summary>How long a confirmation link works.</summary>
    public static readonly TimeSpan VerificationLinkLifetime = TimeSpan.FromHours(24);

    /// <summary>The least time between two emails for one sign-up, or for one address from one
    /// network, so signing up again or resending can't be used to flood someone's inbox. Sign-ups from
    /// other networks aren't held back by it, so a stranger's can't keep the owner from signing up.</summary>
    public static readonly TimeSpan VerificationResendInterval = TimeSpan.FromSeconds(60);

    /// <summary>Sign-ups one address may have waiting at once, each from a different network.</summary>
    private const int MaxPendingPerEmail = 10;

    /// <summary>Wrong passwords a confirmation link takes before it stops working.</summary>
    private const int MaxConfirmAttempts = 5;

    /// <summary>The answer to a wrong password at confirmation while the link still works, so the page can ask again.</summary>
    public const string WrongConfirmationPassword =
        "That isn't the password chosen for this sign-up. If you didn't sign up with this email, you can ignore the email and close this page.";

    /// <summary>Whether new accounts confirm their email first (Auth:EmailVerification).</summary>
    public bool VerifiesEmail => !Auth.Disabled && Auth.EmailVerification.Trim().ToLowerInvariant() switch
    {
        "off" or "false" or "none" => false,
        "required" or "on" or "true" => true,
        _ => mailer.Enabled
    };

    private string VerifyUrl(string token) => $"{links.Value.BaseUrl.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(token)}";

    /// <summary>
    /// A sign-up from the sign-up form. The server's first account, or a server that doesn't verify
    /// email, creates the account at once (<see cref="SignUpAsync"/>). Otherwise every check runs now
    /// (an invitation's too), and a link to confirm the address is emailed; the account follows when
    /// it's opened (<see cref="CompleteSignUpAsync"/>).
    /// </summary>
    public async Task<IdentityResult<SignUpRequested>> RequestSignUpAsync(string email, string password, string? name, string? organizationName,
        string? invitationToken, CancellationToken ct, string? setupToken = null, string? clientAddress = null)
    {
        if (!VerifiesEmail || !await HasAccountsAsync(ct))
        {
            var created = await SignUpAsync(email, password, name, organizationName, invitationToken, ct, setupToken, clientAddress);
            return created.Success ? IdentityResult<SignUpRequested>.Ok(new SignUpRequested(created.Value, null)) : IdentityResult<SignUpRequested>.Fail(created.Error!);
        }

        email = NormalizeEmail(email ?? string.Empty);
        var address = ClientNetwork(clientAddress);
        if (!LooksLikeEmail(email)) return IdentityResult<SignUpRequested>.Fail("Enter a valid email address.");
        if (PasswordHasher.Validate(password) is { } weak) return IdentityResult<SignUpRequested>.Fail(weak);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        InvitationRecord? invitation = null;
        if (!string.IsNullOrWhiteSpace(invitationToken))
        {
            invitation = await FindOpenInvitationAsync(db, invitationToken, ct);
            if (await InvitationProblemAsync(db, invitation, email, ct) is { } invalid) return IdentityResult<SignUpRequested>.Fail(invalid);
        }
        else if (OpenSignUpProblem(email, address) is { } problem)
        {
            return IdentityResult<SignUpRequested>.Fail(problem);
        }

        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return IdentityResult<SignUpRequested>.Fail("An account with this email already exists. Sign in instead.");

        var now = time.GetUtcNow();
        await db.PendingSignUps.Where(p => p.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        var waiting = await db.PendingSignUps.Where(p => p.Email == email).ToListAsync(ct);
        // Each network keeps one sign-up per address, the latest: signing up again (a new password,
        // say) replaces your own earlier one, never a sign-up someone else is waiting on.
        var own = waiting.Where(p => p.ClientAddress == address).ToList();
        if (own.Any(p => now - p.SentAt < VerificationResendInterval))
        {
            return IdentityResult<SignUpRequested>.Fail("We sent a confirmation link to this address less than a minute ago. Check your inbox, or try again in a minute.");
        }

        if (waiting.Count - own.Count >= MaxPendingPerEmail)
        {
            return IdentityResult<SignUpRequested>.Fail("Too many sign-ups are waiting for this address. Check your inbox for a confirmation link, or try again tomorrow.");
        }

        var token = NewVerificationToken();
        var pending = new PendingSignUpRecord
        {
            Id = "ps_" + SecretTokens.New(12),
            TokenHash = SecretTokens.Hash(token),
            Email = email,
            Name = Clip(name, 100) ?? string.Empty,
            OrganizationName = invitation is null ? Clip(organizationName, 100) : null,
            PasswordHash = PasswordHasher.Hash(password),
            ClientAddress = address,
            InvitationId = invitation?.InvitationId,
            CreatedAt = now,
            SentAt = now,
            ExpiresAt = now + VerificationLinkLifetime
        };

        // Sent before anything is saved: if the email can't go, the earlier sign-up and its link stay as they were.
        if (!await SendVerificationAsync(email, Clip(name, 100), token, ct))
        {
            return IdentityResult<SignUpRequested>.Fail("We couldn't send the confirmation email. Check the address and try again in a few minutes.");
        }

        db.PendingSignUps.RemoveRange(own);
        db.PendingSignUps.Add(pending);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Sign-up for {Email} waiting for its email to be confirmed", email);
        return IdentityResult<SignUpRequested>.Ok(new SignUpRequested(null, email, pending.Id));
    }

    private static string NewVerificationToken() => "v_" + SecretTokens.New();

    /// <summary>
    /// Emails a new link for the sign-up <paramref name="pendingId"/> names (the id handed to the
    /// browser that signed up), at most once a minute. The earlier link stops working only once the
    /// new one is sent. It answers the same whether or not that sign-up is still waiting; false means
    /// the email couldn't be sent, and the earlier link still works.
    /// </summary>
    public async Task<bool> ResendSignUpVerificationAsync(string pendingId, CancellationToken ct)
    {
        if (!VerifiesEmail || string.IsNullOrWhiteSpace(pendingId)) return true;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = time.GetUtcNow();
        var pending = await db.PendingSignUps.FindAsync([pendingId.Trim()], ct);
        if (pending is null || pending.ExpiresAt <= now || now - pending.SentAt < VerificationResendInterval) return true;

        var token = NewVerificationToken();
        if (!await SendVerificationAsync(pending.Email, pending.Name.Length > 0 ? pending.Name : null, token, ct)) return false;
        pending.TokenHash = SecretTokens.Hash(token);
        pending.SentAt = now;
        pending.ExpiresAt = now + VerificationLinkLifetime;
        pending.Attempts = 0;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Opens a confirmation link with the password chosen at sign-up: the address is proven, and the
    /// password shows this is the sign-up its owner asked for (not one someone else started with
    /// their address), so the account is created and signed in. The checks run again, since the
    /// server's settings or the invitation may have changed since the link was sent.
    /// </summary>
    public async Task<IdentityResult<SignedIn>> CompleteSignUpAsync(string token, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.Trim().StartsWith("v_", StringComparison.Ordinal))
        {
            return IdentityResult<SignedIn>.Fail("This confirmation link isn't valid. Sign up again to get a new one.");
        }

        if (string.IsNullOrEmpty(password)) return IdentityResult<SignedIn>.Fail("Enter the password you chose when you signed up.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hash = SecretTokens.Hash(token.Trim());
        var pending = await db.PendingSignUps.FirstOrDefaultAsync(p => p.TokenHash == hash, ct);
        var now = time.GetUtcNow();
        if (pending is null || pending.ExpiresAt <= now)
        {
            if (pending is not null) db.PendingSignUps.Remove(pending);
            await db.SaveChangesAsync(ct);
            return IdentityResult<SignedIn>.Fail("This confirmation link has expired or was already used. Sign up again, or sign in if your account is set up.");
        }

        if (!PasswordHasher.Verify(password, pending.PasswordHash))
        {
            // A wrong password leaves the link working, for a few tries.
            if (++pending.Attempts >= MaxConfirmAttempts) db.PendingSignUps.Remove(pending);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Wrong password entered with the confirmation link for {Email}", pending.Email);
            return pending.Attempts >= MaxConfirmAttempts
                ? IdentityResult<SignedIn>.Fail("That isn't the password chosen for this sign-up, and the link no longer works. Sign up again to get a new one.")
                : IdentityResult<SignedIn>.Fail(WrongConfirmationPassword);
        }

        // The address is proven: every sign-up waiting for it ends here, whichever link was opened.
        db.PendingSignUps.RemoveRange(await db.PendingSignUps.Where(p => p.Email == pending.Email).ToListAsync(ct));
        if (await db.Users.AnyAsync(u => u.Email == pending.Email, ct))
        {
            await db.SaveChangesAsync(ct);
            return IdentityResult<SignedIn>.Fail("An account with this email already exists. Sign in instead.");
        }

        InvitationRecord? invitation = null;
        if (pending.InvitationId is { } invitationId)
        {
            invitation = await db.Invitations.FindAsync([invitationId], ct);
            if (await InvitationProblemAsync(db, invitation is not null && IsOpen(invitation) ? invitation : null, pending.Email, ct) is { } invalid)
            {
                await db.SaveChangesAsync(ct);
                return IdentityResult<SignedIn>.Fail(invalid);
            }
        }
        else if (OpenSignUpProblem(pending.Email, pending.ClientAddress) is { } problem)
        {
            await db.SaveChangesAsync(ct);
            return IdentityResult<SignedIn>.Fail(problem);
        }

        var firstAccount = invitation is null && !await db.Users.AnyAsync(ct);
        var created = await CreateAccountAsync(db, pending.Email, pending.PasswordHash, pending.Name.Length > 0 ? pending.Name : null,
            pending.OrganizationName, invitation, firstAccount, pending.ClientAddress, ct);
        if (created.Success) logger.LogInformation("Email {Email} confirmed; account {UserId} created", pending.Email, created.Value!.User.UserId);
        return created;
    }

    /// <summary>Why a sign-up with an operator's email isn't allowed, or null. Nothing proves that
    /// whoever signs up owns the email they type until it's confirmed, and an operator's email opens
    /// the Platform admin portal, so it can't be claimed by signing up, with or without an invitation
    /// (anyone can invite any address into their own organization).</summary>
    private string? OperatorEmailProblem(string email)
    {
        if (!IsOperatorEmail(email)) return null;
        logger.LogWarning("Refused a sign-up with an operator's email ({Email}). To make someone an operator, add their email to " +
                          "Auth:PlatformAdmins after they have an account.", email);
        return "This email can't be used to sign up here. If it's yours, ask whoever runs this server.";
    }

    /// <summary>Why an open sign-up (no invitation, not the first account) isn't allowed, or null.</summary>
    private string? OpenSignUpProblem(string email, string? clientAddress)
    {
        if (OperatorEmailProblem(email) is { } problem) return problem;
        if (!Auth.AllowSignup) return "Sign-up is by invitation only on this server.";
        if (TooManySignUps(clientAddress, time.GetUtcNow()))
        {
            logger.LogWarning("Sign-up from {ClientAddress} refused: too many new organizations from this address recently", clientAddress);
            return "Too many new organizations were created from your network today. Try again tomorrow, or ask to be invited to an existing one.";
        }

        return null;
    }

    /// <summary>Why a sign-up can't join with <paramref name="invitation"/> (null when it isn't open), or null.</summary>
    private async Task<string?> InvitationProblemAsync(AgentDbContext db, InvitationRecord? invitation, string email, CancellationToken ct)
    {
        if (OperatorEmailProblem(email) is { } problem) return problem;
        if (invitation is null) return "This invitation is invalid or has expired.";
        if (invitation.Email != email) return "This invitation was sent to a different email address.";
        if (await IsSuspendedAsync(db, invitation.TenantId, ct)) return SuspendedMessage;
        return null;
    }

    private async Task<bool> HasAccountsAsync(CancellationToken ct)
    {
        if (_hasAccounts) return true;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(ct)) return false;
        _hasAccounts = true;
        return true;
    }

    private async Task<bool> SendVerificationAsync(string email, string? name, string token, CancellationToken ct)
    {
        var url = VerifyUrl(token);
        if (!mailer.Enabled)
        {
            // Auth:EmailVerification=required on a server that can't send email: the operator passes the link on.
            logger.LogWarning("Email isn't set up on this server (Email:Smtp), so the confirmation link for {Email} wasn't sent. Open it to finish the sign-up: {Url}", email, url);
            return true;
        }

        return await mailer.SendAsync(email, VerificationEmail(name, url), ct);
    }

    public static PlatformEmail VerificationEmail(string? name, string url) => new()
    {
        Subject = $"Confirm your email for {PlatformEmailTemplate.ProductName}",
        Heading = "Confirm your email address",
        Greeting = name is { Length: > 0 } ? $"Hi {name}," : "Hi,",
        Paragraphs = [$"Thanks for signing up for {PlatformEmailTemplate.ProductName}. Confirm this is your email address to finish setting up your account. You'll be asked for the password you chose when you signed up."],
        Action = new EmailAction("Confirm email address", url),
        Footnote = $"This link expires in {VerificationLinkLifetime.TotalHours:0} hours. If you didn't sign up, you can ignore this email: no account is created unless the link is opened with the password chosen at sign-up."
    };
}
