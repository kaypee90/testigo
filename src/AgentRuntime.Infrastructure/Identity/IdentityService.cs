using AgentRuntime.Infrastructure.Persistence;
using AgentRuntime.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentRuntime.Infrastructure.Identity;

/// <summary>The staff dashboard's public address, for links in emails (section "Dashboard").</summary>
public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>e.g. https://panel.example.org.</summary>
    public string BaseUrl { get; set; } = "http://localhost:3000";
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>"accounts" (sign-in required) or "disabled" (single-user: everyone is the owner of
    /// the default organization; only for a machine nobody else can reach).</summary>
    public string Mode { get; set; } = "accounts";

    public bool Disabled => string.Equals(Mode, "disabled", StringComparison.OrdinalIgnoreCase);

    /// <summary>Anyone can create an account (and their own organization). Off: only invitations.
    /// The very first account can always be created.</summary>
    public bool AllowSignup { get; set; } = true;

    /// <summary>The first account on a server with none (it owns everything there, and may use an
    /// operator's email) must present the setup token. Off only for a machine nobody else can reach.</summary>
    public bool SetupTokenRequired { get; set; } = true;

    /// <summary>The setup token. Empty: one is generated at each start and written to the log
    /// while the server has no accounts.</summary>
    public string? SetupToken { get; set; }

    /// <summary>Emails of the operators, who see every organization in Platform admin.</summary>
    public List<string> PlatformAdmins { get; set; } = [];

    /// <summary>Wrong passwords one account may get within <see cref="IdentityService.WrongPasswordWindow"/>
    /// before sign-in to it pauses, wherever the attempts come from (the per-address limit alone
    /// doesn't stop guessing from many addresses). 0: no limit.</summary>
    public int WrongPasswordsPerAccount { get; set; } = 10;

    /// <summary>New organizations one client address may create by signing up within
    /// <see cref="IdentityService.SignUpWindow"/>, so a script can't fill the server with organizations
    /// from one address. Joining by invitation doesn't count. 0: no limit.</summary>
    public int SignUpsPerAddressPerDay { get; set; } = 10;

    /// <summary>Whether an open sign-up has to confirm its email before the account exists
    /// (docs/platform.md#email-verification): "auto" (the default) when this server can send email
    /// (Email:Smtp), "required" always (without email, the links are written to the API's log), "off"
    /// never. Invitations and the first account (setup token) are never asked.</summary>
    public string EmailVerification { get; set; } = "auto";

    public int SessionDays { get; set; } = 30;

    /// <summary>How long a session lasts, sliding, in hours (Testigo NFR-SEC-8: 8 by default).
    /// Zero or less falls back to <see cref="SessionDays"/>.</summary>
    public int SessionHours { get; set; } = 8;

    /// <summary>The session lifetime from these settings.</summary>
    public TimeSpan SessionLifetime => SessionHours > 0 ? TimeSpan.FromHours(SessionHours) : TimeSpan.FromDays(Math.Max(1, SessionDays));
    public int InvitationDays { get; set; } = 7;
    public string CookieName { get; set; } = "testigo_session";

    /// <summary>"auto": the session cookie is <c>Secure</c> when the request came over HTTPS (behind a
    /// proxy, that needs ForwardedHeaders:Enabled). "always": it's always <c>Secure</c>, for a server
    /// only ever reached over HTTPS; browsers still accept it on http://localhost.</summary>
    public string CookieSecure { get; set; } = "auto";

    public bool AlwaysSecureCookie => string.Equals(CookieSecure, "always", StringComparison.OrdinalIgnoreCase);

    /// <summary>A built-in sign-in for a local machine, created at startup if it doesn't exist yet.
    /// Leave the username empty to have none; never enable it on a server others can reach.</summary>
    public LocalAdminOptions LocalAdmin { get; set; } = new();
}

public sealed class LocalAdminOptions
{
    /// <summary>What to type in the sign-in form's email field (it needn't be an email address).</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(Password);
}

/// <summary>Stops a server that others can reach from starting with a guessable local admin
/// (the local default is admin / admin, an owner and operator).</summary>
public static class LocalAdminGuard
{
    /// <summary>Why the server must not start, or null. <paramref name="publicServer"/>: it's set up
    /// to sit behind a TLS proxy (ForwardedHeaders:Enabled, or Auth:CookieSecure=always).</summary>
    public static string? Problem(AuthOptions auth, bool publicServer) =>
        !auth.Disabled && auth.LocalAdmin.Enabled && publicServer && PasswordHasher.Validate(auth.LocalAdmin.Password) is not null
            ? $"Auth:LocalAdmin ('{auth.LocalAdmin.Username}') has a password shorter than {PasswordHasher.MinLength} characters on a server set up for the internet " +
              "(ForwardedHeaders:Enabled or Auth:CookieSecure=always). That account is an owner and operator. Set LOCAL_ADMIN_USERNAME= (empty) to " +
              "turn it off, or give it a strong LOCAL_ADMIN_PASSWORD. If the account was already created, change or remove it too: clearing the setting doesn't."
            : null;
}

/// <summary>Who is making a request, as the API sees it.</summary>
public sealed record Caller(string TenantId, TenantRole Role, string? UserId, string? Email, bool PlatformAdmin, string? SessionHash)
{
    public string ActorId => UserId ?? "local";
}

public sealed record IdentityResult<T>(T? Value, string? Error)
{
    public bool Success => Error is null;
    public static IdentityResult<T> Ok(T value) => new(value, null);
    public static IdentityResult<T> Fail(string error) => new(default, error);
}

/// <summary>A sign-in that worked. With two-factor sign-in on, a right password yields no session
/// yet: <see cref="TwoFactorChallenge"/> is handed back with the code (TwoFactorService).</summary>
public sealed record SignedIn(string? SessionToken, UserRecord User, string TenantId, string? TwoFactorChallenge = null)
{
    public bool NeedsSecondFactor => TwoFactorChallenge is not null;
}

public sealed record MembershipView(string TenantId, string TenantName, TenantRole Role, bool Suspended = false);

public sealed record MemberView(string UserId, string Email, string Name, TenantRole Role, DateTimeOffset JoinedAt);

public sealed record InvitationView(string InvitationId, string Email, TenantRole Role, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt);

/// <summary>
/// Accounts, organizations, memberships, sessions and invitations (docs/platform.md).
/// Secrets (passwords, session tokens, invitation links) are only ever stored hashed.
/// </summary>
public sealed partial class IdentityService(
    IDbContextFactory<AgentDbContext> dbFactory,
    IOptions<AuthOptions> authOptions,
    TimeProvider time,
    PlatformMailer mailer,
    IOptions<DashboardOptions> links,
    ILogger<IdentityService> logger)
{
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);
    // Verified against when an email is unknown, so a login takes as long either way.
    private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash(SecretTokens.New()));

    private AuthOptions Auth => authOptions.Value;

    public static readonly TimeSpan WrongPasswordWindow = TimeSpan.FromMinutes(15);
    private const int MaxTrackedEmails = 50_000;

    /// <summary>Guesses from all addresses together get this many times the per-address limit
    /// before the account itself is locked.</summary>
    public const int AnyAddressFactor = 10;

    // Wrong passwords per email (known or not, so the answer doesn't say which), in this process:
    // counted per client address, and for the email as a whole. Locking on the email alone would
    // let anyone who knows it keep its owner out by typing wrong passwords, so the limit applies to
    // the address that's guessing. The email as a whole locks only at AnyAddressFactor times that,
    // which bounds guessing spread over many addresses.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTimeOffset Since)> _wrongPasswords = new();

    // The two kinds of key can't be mistaken for each other, whatever is typed as the email: one
    // made to look like someone's address key ("them@example.com|203.0.113.7") counts as an email.
    private static string EmailKey(string email) => "e:" + email;
    private static string AddressKey(string email, string address) => $"a:{address}|{email}";

    /// <summary>What an address is counted as: itself for IPv4, its /64 network for IPv6, where one
    /// subscriber has a whole network of addresses to move between. Null when there's none.</summary>
    internal static string? ClientNetwork(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        if (!System.Net.IPAddress.TryParse(address.Trim(), out var ip)) return address.Trim();
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new System.Net.IPAddress(bytes) + "/64";
    }

    private bool Reached(string key, int limit, DateTimeOffset now) =>
        _wrongPasswords.TryGetValue(key, out var w) && now - w.Since < WrongPasswordWindow && w.Count >= limit;

    private bool TooManyWrongPasswords(string email, string? clientAddress, DateTimeOffset now)
    {
        var limit = Auth.WrongPasswordsPerAccount;
        if (limit <= 0) return false;
        // Without an address to tell callers apart, the email is all there is to count by.
        if (string.IsNullOrEmpty(clientAddress)) return Reached(EmailKey(email), limit, now);
        return Reached(AddressKey(email, clientAddress), limit, now) || Reached(EmailKey(email), limit * AnyAddressFactor, now);
    }

    private void WrongPassword(string email, string? clientAddress, DateTimeOffset now)
    {
        // Nothing that long is an account's email, and what's typed is kept as a key until its window ends.
        if (Auth.WrongPasswordsPerAccount <= 0 || email.Length > MaxEmailLength) return;
        Count(EmailKey(email), now);
        if (!string.IsNullOrEmpty(clientAddress)) Count(AddressKey(email, clientAddress), now);
    }

    private void Count(string key, DateTimeOffset now)
    {
        if (_wrongPasswords.Count >= MaxTrackedEmails)
        {
            foreach (var (old, w) in _wrongPasswords)
            {
                if (now - w.Since >= WrongPasswordWindow) _wrongPasswords.TryRemove(old, out _);
            }

            // Still full of live entries: a flood of made-up emails. Start over rather than grow.
            if (_wrongPasswords.Count >= MaxTrackedEmails) _wrongPasswords.Clear();
        }

        _wrongPasswords.AddOrUpdate(key, (1, now), (_, w) => now - w.Since >= WrongPasswordWindow ? (1, now) : (w.Count + 1, w.Since));
    }

    public static readonly TimeSpan SignUpWindow = TimeSpan.FromHours(24);
    private const int MaxTrackedAddresses = 50_000;

    // Organizations created by signing up, per client address, in this process.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTimeOffset Since)> _signUps = new();

    private bool TooManySignUps(string? clientAddress, DateTimeOffset now) =>
        Auth.SignUpsPerAddressPerDay > 0 && !string.IsNullOrEmpty(clientAddress) && _signUps.TryGetValue(clientAddress, out var s) &&
        now - s.Since < SignUpWindow && s.Count >= Auth.SignUpsPerAddressPerDay;

    private void SignedUp(string? clientAddress, DateTimeOffset now)
    {
        if (Auth.SignUpsPerAddressPerDay <= 0 || string.IsNullOrEmpty(clientAddress)) return;
        if (_signUps.Count >= MaxTrackedAddresses)
        {
            foreach (var (key, s) in _signUps)
            {
                if (now - s.Since >= SignUpWindow) _signUps.TryRemove(key, out _);
            }
        }

        _signUps.AddOrUpdate(clientAddress, (1, now), (_, s) => now - s.Since >= SignUpWindow ? (1, now) : (s.Count + 1, s.Since));
    }

    // Used when Auth:SetupToken isn't set; new at every start.
    private readonly string _generatedSetupToken = SecretTokens.New(18);
    // Accounts are never all deleted again, so once one exists setup is over for good.
    private volatile bool _hasAccounts;

    private string SetupToken => string.IsNullOrWhiteSpace(Auth.SetupToken) ? _generatedSetupToken : Auth.SetupToken.Trim();

    /// <summary>Whether this server still waits for its first account, which needs the setup token.</summary>
    public async Task<bool> SetupPendingAsync(CancellationToken ct = default)
    {
        if (Auth.Disabled || !Auth.SetupTokenRequired || _hasAccounts) return false;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(ct)) return true;
        _hasAccounts = true;
        return false;
    }

    /// <summary>At startup on a server with no accounts: tells the operator how to create the first one.</summary>
    public async Task AnnounceSetupAsync(CancellationToken ct = default)
    {
        if (!await SetupPendingAsync(ct)) return;
        if (string.IsNullOrWhiteSpace(Auth.SetupToken))
        {
            logger.LogWarning("This server has no accounts yet. Create the first one (its owner) at /login?mode=signup with this setup token: " +
                              "{SetupToken}. It changes at every restart; set Auth:SetupToken to choose your own.", _generatedSetupToken);
        }
        else
        {
            logger.LogWarning("This server has no accounts yet. Create the first one (its owner) at /login?mode=signup with the setup token in Auth:SetupToken.");
        }
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private const int MaxEmailLength = 254;

    private static bool LooksLikeEmail(string email) =>
        email.Length is > 3 and <= MaxEmailLength && email.IndexOf('@') is > 0 and var at && at < email.Length - 1 && !email.Contains(' ');

    public bool IsPlatformAdmin(string? email) => Auth.Disabled || (email is not null && IsOperatorEmail(email));

    /// <summary>An email the configuration names as an operator's (Auth:PlatformAdmins, or the local admin).</summary>
    private bool IsOperatorEmail(string email) =>
        Auth.PlatformAdmins.Any(a => NormalizeEmail(a) == email)
        || (Auth.LocalAdmin.Enabled && NormalizeEmail(Auth.LocalAdmin.Username!) == email);

    /// <summary>Creates the configured local admin (Auth:LocalAdmin) as an owner of the default
    /// organization. An existing account is left alone, so a changed password survives restarts.
    /// The password skips the strength rules on purpose: it's the operator's choice for a local box.</summary>
    public async Task EnsureLocalAdminAsync(CancellationToken ct = default)
    {
        if (Auth.Disabled || !Auth.LocalAdmin.Enabled) return;
        var username = NormalizeEmail(Auth.LocalAdmin.Username!);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(u => u.Email == username, ct)) return;

        var now = DateTimeOffset.UtcNow;
        var user = new UserRecord
        {
            UserId = "u-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
            Email = username,
            Name = "Local admin",
            PasswordHash = PasswordHasher.Hash(Auth.LocalAdmin.Password!),
            CreatedAt = now
        };
        db.Users.Add(user);
        if (await db.Tenants.FindAsync([TenantIds.Default], ct) is null)
        {
            db.Tenants.Add(new TenantRecord { TenantId = TenantIds.Default, Name = "Default organization", CreatedAt = now });
        }

        db.Memberships.Add(new MembershipRecord { TenantId = TenantIds.Default, UserId = user.UserId, Role = nameof(TenantRole.Owner), CreatedAt = now });
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Created the local admin account '{Username}'. Remove Auth:LocalAdmin on any server others can reach.", username);
    }

    // ---- Accounts ---------------------------------------------------------------

    /// <summary>Creates an account. With an invitation it joins that organization; otherwise it
    /// gets a new one of its own. The very first account also takes over the default organization,
    /// which holds everything created before accounts existed.</summary>
    public async Task<IdentityResult<SignedIn>> SignUpAsync(string email, string password, string? name, string? organizationName,
        string? invitationToken, CancellationToken ct, string? setupToken = null, string? clientAddress = null)
    {
        email = NormalizeEmail(email ?? string.Empty);
        clientAddress = ClientNetwork(clientAddress);
        if (!LooksLikeEmail(email)) return IdentityResult<SignedIn>.Fail("Enter a valid email address.");
        if (PasswordHasher.Validate(password) is { } weak) return IdentityResult<SignedIn>.Fail(weak);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return IdentityResult<SignedIn>.Fail("An account with this email already exists. Sign in instead.");

        var firstAccount = !await db.Users.AnyAsync(ct);
        // The first account owns everything on the server, whatever the sign-up setting: it has to
        // be whoever runs it, not whoever finds it first.
        if (firstAccount && !Auth.Disabled && Auth.SetupTokenRequired &&
            (string.IsNullOrWhiteSpace(setupToken) || !SecretTokens.HashEquals(setupToken.Trim(), SecretTokens.Hash(SetupToken))))
        {
            return IdentityResult<SignedIn>.Fail("This server has no accounts yet, and creating the first one takes its setup token. " +
                                                 "Whoever runs the server has it (SETUP_TOKEN in its settings, or the API's startup log).");
        }

        // The open sign-up checks don't apply to the first account: it's how a new server gets its operator.
        InvitationRecord? invitation = null;
        if (!string.IsNullOrWhiteSpace(invitationToken))
        {
            invitation = await FindOpenInvitationAsync(db, invitationToken, ct);
            if (await InvitationProblemAsync(db, invitation, email, ct) is { } invalid) return IdentityResult<SignedIn>.Fail(invalid);
        }
        else if (!firstAccount && OpenSignUpProblem(email, clientAddress) is { } problem)
        {
            return IdentityResult<SignedIn>.Fail(problem);
        }

        return await CreateAccountAsync(db, email, PasswordHasher.Hash(password), name, organizationName, invitation, firstAccount, clientAddress, ct);
    }

    /// <summary>Creates the account once every check has passed (here, or when a confirmed sign-up
    /// completes): the user, their organization or the invited one, and a session.</summary>
    private async Task<IdentityResult<SignedIn>> CreateAccountAsync(AgentDbContext db, string email, string passwordHash, string? name,
        string? organizationName, InvitationRecord? invitation, bool firstAccount, string? clientAddress, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new UserRecord
        {
            UserId = "u-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
            Email = email,
            Name = Clip(name, 100) ?? email.Split('@')[0],
            PasswordHash = passwordHash,
            CreatedAt = now,
            LastLoginAt = now
        };
        db.Users.Add(user);

        string tenantId;
        if (invitation is not null)
        {
            tenantId = invitation.TenantId;
            invitation.AcceptedAt = now;
            db.Memberships.Add(new MembershipRecord { TenantId = tenantId, UserId = user.UserId, Role = invitation.Role, CreatedAt = now });
        }
        else
        {
            var orgName = Clip(organizationName, 100) ?? $"{user.Name}'s organization";
            if (firstAccount && await db.Tenants.FindAsync([TenantIds.Default], ct) is null)
            {
                tenantId = TenantIds.Default;
            }
            else
            {
                tenantId = TenantIds.New();
            }

            db.Tenants.Add(new TenantRecord { TenantId = tenantId, Name = orgName, CreatedAt = now });
            db.Memberships.Add(new MembershipRecord { TenantId = tenantId, UserId = user.UserId, Role = nameof(TenantRole.Owner), CreatedAt = now });
        }

        var token = NewSession(db, user.UserId, tenantId, now);
        await db.SaveChangesAsync(ct);
        if (invitation is null && !firstAccount) SignedUp(clientAddress, time.GetUtcNow());
        logger.LogInformation("Account {UserId} created in tenant {TenantId}", user.UserId, tenantId);
        return IdentityResult<SignedIn>.Ok(new SignedIn(token, user, tenantId));
    }

    /// <param name="clientAddress">Where the attempt comes from, so wrong passwords from one
    /// address don't lock the account's owner out from theirs.</param>
    public async Task<IdentityResult<SignedIn>> SignInAsync(string email, string password, CancellationToken ct, string? clientAddress = null)
    {
        email = NormalizeEmail(email ?? string.Empty);
        clientAddress = ClientNetwork(clientAddress);
        var attemptAt = time.GetUtcNow();
        if (TooManyWrongPasswords(email, clientAddress, attemptAt))
        {
            logger.LogWarning("Sign-in for {Email} refused: too many wrong passwords recently", email);
            return IdentityResult<SignedIn>.Fail($"Too many wrong passwords for this account. Try again in {WrongPasswordWindow.TotalMinutes:0} minutes.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            PasswordHasher.Verify(password ?? string.Empty, DummyHash.Value);
            WrongPassword(email, clientAddress, attemptAt);
            return IdentityResult<SignedIn>.Fail("Wrong email or password.");
        }

        if (!PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash))
        {
            WrongPassword(email, clientAddress, attemptAt);
            return IdentityResult<SignedIn>.Fail("Wrong email or password.");
        }

        _wrongPasswords.TryRemove(EmailKey(email), out _);
        if (!string.IsNullOrEmpty(clientAddress)) _wrongPasswords.TryRemove(AddressKey(email, clientAddress), out _);

        var memberships = await db.Memberships.Where(m => m.UserId == user.UserId).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        if (memberships.Count == 0) return IdentityResult<SignedIn>.Fail("This account doesn't belong to any organization any more.");
        // Sign in to the first organization that isn't suspended.
        var tenantIds = memberships.Select(m => m.TenantId).ToList();
        var suspended = await db.Tenants.Where(t => tenantIds.Contains(t.TenantId) && t.Status == TenantStatuses.Suspended).Select(t => t.TenantId).ToListAsync(ct);
        var membership = memberships.FirstOrDefault(m => !suspended.Contains(m.TenantId));
        if (membership is null) return IdentityResult<SignedIn>.Fail(SuspendedMessage);

        if (PasswordHasher.NeedsRehash(user.PasswordHash)) user.PasswordHash = PasswordHasher.Hash(password!);
        var now = time.GetUtcNow();
        if (user.TwoFactorEnabledAt is not null)
        {
            // The password was right; the session waits for the authenticator code.
            if (await TwoFactorService.TooManyWrongCodesAsync(db, user.UserId, now, ct))
            {
                logger.LogWarning("Sign-in for {UserId} refused: too many wrong two-factor codes recently", user.UserId);
                return IdentityResult<SignedIn>.Fail($"Too many wrong codes for this account. Try again in {TwoFactorService.WrongCodeWindow.TotalMinutes:0} minutes.");
            }

            // Challenges are kept for the wrong-code window, then cleared here and on success.
            var stale = now - TwoFactorService.WrongCodeWindow;
            await db.LoginChallenges.Where(c => c.UserId == user.UserId && c.CreatedAt <= stale).ExecuteDeleteAsync(ct);
            var challenge = NewLoginChallenge(db, user.UserId, membership.TenantId, now);
            await db.SaveChangesAsync(ct);
            return IdentityResult<SignedIn>.Ok(new SignedIn(null, user, membership.TenantId, challenge));
        }

        user.LastLoginAt = now;
        var token = NewSession(db, user.UserId, membership.TenantId, now);
        await db.SaveChangesAsync(ct);
        return IdentityResult<SignedIn>.Ok(new SignedIn(token, user, membership.TenantId));
    }

    /// <summary>How long the code step of a sign-in may take.</summary>
    public static readonly TimeSpan LoginChallengeLifetime = TimeSpan.FromMinutes(5);

    private static string NewLoginChallenge(AgentDbContext db, string userId, string tenantId, DateTimeOffset now)
    {
        var token = "c_" + SecretTokens.New();
        db.LoginChallenges.Add(new LoginChallengeRecord
        {
            TokenHash = SecretTokens.Hash(token),
            UserId = userId,
            TenantId = tenantId,
            CreatedAt = now,
            ExpiresAt = now + LoginChallengeLifetime
        });
        return token;
    }

    internal string NewSession(AgentDbContext db, string userId, string tenantId, DateTimeOffset now)
    {
        var token = "s_" + SecretTokens.New();
        db.Sessions.Add(new SessionRecord
        {
            TokenHash = SecretTokens.Hash(token),
            UserId = userId,
            TenantId = tenantId,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.Add(Auth.SessionLifetime)
        });
        return token;
    }

    /// <summary>Resolves a session cookie, sliding its expiry. Null if it's unknown, expired, or the
    /// user no longer belongs to the organization it was working in.</summary>
    public async Task<Caller?> ResolveSessionAsync(string token, CancellationToken ct)
    {
        if (!token.StartsWith("s_", StringComparison.Ordinal)) return null;
        var hash = SecretTokens.Hash(token);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.FindAsync([hash], ct);
        var now = DateTimeOffset.UtcNow;
        if (session is null || session.ExpiresAt <= now) return null;

        var membership = await db.Memberships.FindAsync([session.TenantId, session.UserId], ct);
        var user = await db.Users.FindAsync([session.UserId], ct);
        if (membership is null || user is null || await IsSuspendedAsync(db, session.TenantId, ct)) return null;

        if (now - session.LastSeenAt > TouchInterval)
        {
            session.LastSeenAt = now;
            session.ExpiresAt = now.Add(Auth.SessionLifetime);
            await db.SaveChangesAsync(ct);
        }

        return new Caller(session.TenantId, ParseRole(membership.Role), user.UserId, user.Email, IsPlatformAdmin(user.Email), hash);
    }

    public async Task SignOutAsync(string sessionHash, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Sessions.Where(s => s.TokenHash == sessionHash).ExecuteDeleteAsync(ct);
    }

    public async Task<bool> SwitchTenantAsync(string sessionHash, string userId, string tenantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Memberships.FindAsync([tenantId, userId], ct) is null || await IsSuspendedAsync(db, tenantId, ct)) return false;
        var session = await db.Sessions.FindAsync([sessionHash], ct);
        if (session is null || session.UserId != userId) return false;
        session.TenantId = tenantId;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IdentityResult<bool>> ChangePasswordAsync(string userId, string currentPassword, string newPassword, string keepSessionHash, CancellationToken ct)
    {
        if (PasswordHasher.Validate(newPassword) is { } weak) return IdentityResult<bool>.Fail(weak);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null || !PasswordHasher.Verify(currentPassword, user.PasswordHash)) return IdentityResult<bool>.Fail("The current password is wrong.");
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync(ct);
        // Every other browser has to sign in again, and a sign-in started with the old password
        // waiting for its code is over. (Its wrong codes stop counting towards the limit too.)
        await db.Sessions.Where(s => s.UserId == userId && s.TokenHash != keepSessionHash).ExecuteDeleteAsync(ct);
        await db.LoginChallenges.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        return IdentityResult<bool>.Ok(true);
    }

    // ---- Deleting an account ----------------------------------------------------------

    /// <summary>What deleting an account does to each organization it belongs to: organizations it
    /// would leave (others stay), organizations it alone belongs to (closed with it), and
    /// organizations that block it (it's their only owner, but others are members).</summary>
    public sealed record AccountDeletionPlan(
        bool IsOperator,
        IReadOnlyList<MembershipView> Leaves,
        IReadOnlyList<MembershipView> Closes,
        IReadOnlyList<MembershipView> Blocks)
    {
        public bool Allowed => !IsOperator && Blocks.Count == 0;
    }

    public async Task<AccountDeletionPlan?> PlanAccountDeletionAsync(string userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user is null) return null;

        List<MembershipView> leaves = [], closes = [], blocks = [];
        foreach (var m in await MembershipsAsync(userId, ct))
        {
            var others = await db.Memberships.CountAsync(x => x.TenantId == m.TenantId && x.UserId != userId, ct);
            if (others == 0) closes.Add(m);
            else if (m.Role == TenantRole.Owner && await OwnerCountAsync(db, m.TenantId, ct) <= 1) blocks.Add(m);
            else leaves.Add(m);
        }

        return new AccountDeletionPlan(IsOperatorEmail(user.Email), leaves, closes, blocks);
    }

    public async Task<bool> VerifyPasswordAsync(string userId, string password, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hash = await db.Users.Where(u => u.UserId == userId).Select(u => u.PasswordHash).FirstOrDefaultAsync(ct);
        return hash is not null && PasswordHasher.Verify(password, hash);
    }

    /// <summary>
    /// Removes the account and what identifies the person: their memberships, sessions, pending
    /// sign-ins and open invitations for their email. What they did on cases stays with each
    /// organization, which owns it, and in the audit log. The organizations they alone belonged to must be closed first (AccountDeletionService).
    /// </summary>
    public async Task DeleteUserAsync(string userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return;

        await db.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        await db.LoginChallenges.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Memberships.Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Invitations.Where(i => i.Email == user.Email && i.AcceptedAt == null).ExecuteDeleteAsync(ct);
        await db.Users.Where(u => u.UserId == userId).ExecuteDeleteAsync(ct);
        logger.LogWarning("Account {UserId} was deleted by its owner", userId);
    }

    public async Task<IReadOnlyList<MembershipView>> MembershipsAsync(string userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Memberships.Where(m => m.UserId == userId)
            .Join(db.Tenants, m => m.TenantId, t => t.TenantId, (m, t) => new { m, t })
            .OrderBy(x => x.m.CreatedAt)
            .Select(x => new MembershipView(x.t.TenantId, x.t.Name, ParseRole(x.m.Role), x.t.Status == TenantStatuses.Suspended))
            .ToListAsync(ct);
    }

    // ---- Organizations and members --------------------------------------------------

    public async Task<TenantRecord?> GetTenantAsync(string tenantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
    }

    public async Task RenameTenantAsync(string tenantId, string name, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FindAsync([tenantId], ct);
        if (tenant is null || Clip(name, 100) is not { } clipped) return;
        tenant.Name = clipped;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MemberView>> MembersAsync(string tenantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Memberships.Where(m => m.TenantId == tenantId)
            .Join(db.Users, m => m.UserId, u => u.UserId, (m, u) => new { m, u })
            .OrderBy(x => x.m.CreatedAt)
            .Select(x => new MemberView(x.u.UserId, x.u.Email, x.u.Name, ParseRole(x.m.Role), x.m.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<IdentityResult<bool>> SetRoleAsync(Caller caller, string userId, TenantRole role, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var membership = await db.Memberships.FindAsync([caller.TenantId, userId], ct);
        if (membership is null) return IdentityResult<bool>.Fail("No such member.");
        var current = ParseRole(membership.Role);
        // Only owners make or unmake owners; admins manage everyone below them.
        if ((role == TenantRole.Owner || current == TenantRole.Owner) && caller.Role != TenantRole.Owner)
        {
            return IdentityResult<bool>.Fail("Only an owner can change who is an owner.");
        }

        if (current == TenantRole.Owner && role != TenantRole.Owner && await OwnerCountAsync(db, caller.TenantId, ct) <= 1)
        {
            return IdentityResult<bool>.Fail("An organization needs at least one owner.");
        }

        membership.Role = role.ToString();
        await db.SaveChangesAsync(ct);
        return IdentityResult<bool>.Ok(true);
    }

    public async Task<IdentityResult<bool>> RemoveMemberAsync(Caller caller, string userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var membership = await db.Memberships.FindAsync([caller.TenantId, userId], ct);
        if (membership is null) return IdentityResult<bool>.Fail("No such member.");
        var role = ParseRole(membership.Role);
        if (role == TenantRole.Owner && caller.Role != TenantRole.Owner && caller.UserId != userId)
        {
            return IdentityResult<bool>.Fail("Only an owner can remove an owner.");
        }

        if (role == TenantRole.Owner && await OwnerCountAsync(db, caller.TenantId, ct) <= 1)
        {
            return IdentityResult<bool>.Fail("An organization needs at least one owner.");
        }

        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);
        await db.Sessions.Where(s => s.UserId == userId && s.TenantId == caller.TenantId).ExecuteDeleteAsync(ct);
        return IdentityResult<bool>.Ok(true);
    }

    private static Task<int> OwnerCountAsync(AgentDbContext db, string tenantId, CancellationToken ct) =>
        db.Memberships.CountAsync(m => m.TenantId == tenantId && m.Role == nameof(TenantRole.Owner), ct);

    // ---- Invitations --------------------------------------------------------------

    /// <summary>Creates an invitation and returns its one-time token (for the invitation link).</summary>
    public async Task<IdentityResult<(InvitationView Invitation, string Token)>> InviteAsync(Caller caller, string email, TenantRole role, CancellationToken ct)
    {
        email = NormalizeEmail(email ?? string.Empty);
        if (!LooksLikeEmail(email)) return IdentityResult<(InvitationView, string)>.Fail("Enter a valid email address.");
        if (role > caller.Role) return IdentityResult<(InvitationView, string)>.Fail("You can't invite someone with more access than you have.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Memberships.Join(db.Users, m => m.UserId, u => u.UserId, (m, u) => new { m, u })
                .AnyAsync(x => x.m.TenantId == caller.TenantId && x.u.Email == email, ct))
        {
            return IdentityResult<(InvitationView, string)>.Fail("They're already a member.");
        }

        return await CreateInvitationAsync(db, caller.TenantId, email, role, caller.ActorId, ct);
    }

    /// <summary>A platform operator inviting someone into any organization, with any role
    /// (docs/platform.md#platform-administration).</summary>
    public async Task<IdentityResult<(InvitationView Invitation, string Token)>> InviteAsOperatorAsync(string tenantId, string email, TenantRole role,
        string operatorId, CancellationToken ct)
    {
        email = NormalizeEmail(email ?? string.Empty);
        if (!LooksLikeEmail(email)) return IdentityResult<(InvitationView, string)>.Fail("Enter a valid email address.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await CreateInvitationAsync(db, tenantId, email, role, operatorId, ct);
    }

    private async Task<IdentityResult<(InvitationView Invitation, string Token)>> CreateInvitationAsync(AgentDbContext db, string tenantId, string email,
        TenantRole role, string invitedBy, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // A newer invitation for the same person replaces any still open, so only one link works.
        await db.Invitations.Where(i => i.TenantId == tenantId && i.Email == email && i.AcceptedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.RevokedAt, now), ct);

        var token = "inv_" + SecretTokens.New();
        var invitation = new InvitationRecord
        {
            InvitationId = "inv-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
            TenantId = tenantId,
            Email = email,
            Role = role.ToString(),
            TokenHash = SecretTokens.Hash(token),
            InvitedByUserId = invitedBy,
            CreatedAt = now,
            ExpiresAt = now.AddDays(Math.Max(1, Auth.InvitationDays))
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return IdentityResult<(InvitationView, string)>.Ok((ToView(invitation), token));
    }

    public async Task<IReadOnlyList<InvitationView>> InvitationsAsync(string tenantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Invitations.AsNoTracking().Where(i => i.TenantId == tenantId).OrderByDescending(i => i.CreatedAt).Take(100).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }

    public async Task<bool> RevokeInvitationAsync(string tenantId, string invitationId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.TenantId == tenantId && i.InvitationId == invitationId, ct);
        if (invitation is null || invitation.AcceptedAt is not null) return false;
        invitation.RevokedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>What an invitation link is for, so the sign-up page can show it. Null if it's not usable.</summary>
    public async Task<(string TenantName, string Email, TenantRole Role)?> PeekInvitationAsync(string token, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var invitation = await FindOpenInvitationAsync(db, token, ct);
        if (invitation is null || await IsSuspendedAsync(db, invitation.TenantId, ct)) return null;
        var tenant = await db.Tenants.FindAsync([invitation.TenantId], ct);
        return (tenant?.Name ?? "an organization", invitation.Email, ParseRole(invitation.Role));
    }

    /// <summary>A signed-in user accepting an invitation sent to their email.</summary>
    public async Task<IdentityResult<string>> AcceptInvitationAsync(string userId, string token, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var invitation = await FindOpenInvitationAsync(db, token, ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (invitation is null || user is null) return IdentityResult<string>.Fail("This invitation is invalid or has expired.");
        if (invitation.Email != user.Email) return IdentityResult<string>.Fail("This invitation was sent to a different email address.");
        if (await IsSuspendedAsync(db, invitation.TenantId, ct)) return IdentityResult<string>.Fail(SuspendedMessage);

        if (await db.Memberships.FindAsync([invitation.TenantId, userId], ct) is null)
        {
            db.Memberships.Add(new MembershipRecord { TenantId = invitation.TenantId, UserId = userId, Role = invitation.Role, CreatedAt = DateTimeOffset.UtcNow });
        }

        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return IdentityResult<string>.Ok(invitation.TenantId);
    }

    private static async Task<InvitationRecord?> FindOpenInvitationAsync(AgentDbContext db, string token, CancellationToken ct)
    {
        var hash = SecretTokens.Hash(token.Trim());
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        return invitation is not null && IsOpen(invitation) ? invitation : null;
    }

    private static bool IsOpen(InvitationRecord invitation) =>
        invitation is { AcceptedAt: null, RevokedAt: null } && invitation.ExpiresAt > DateTimeOffset.UtcNow;

    private static InvitationView ToView(InvitationRecord i) =>
        new(i.InvitationId, i.Email, ParseRole(i.Role), i.CreatedAt, i.ExpiresAt, i.AcceptedAt, i.RevokedAt);

    // ---- Helpers ------------------------------------------------------------------------

    public const string SuspendedMessage = "This organization's access is suspended. Contact the platform operator.";

    internal static Task<bool> IsSuspendedAsync(AgentDbContext db, string tenantId, CancellationToken ct) =>
        db.Tenants.AnyAsync(t => t.TenantId == tenantId && t.Status == TenantStatuses.Suspended, ct);

    public static TenantRole ParseRole(string role) => Enum.TryParse<TenantRole>(role, ignoreCase: true, out var r) ? r : TenantRole.Viewer;

    private static string? Clip(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
