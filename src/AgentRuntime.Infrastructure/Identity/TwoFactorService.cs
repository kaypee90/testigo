using System.Security.Cryptography;
using AgentRuntime.Infrastructure.Persistence;
using AgentRuntime.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QRCoder;

namespace AgentRuntime.Infrastructure.Identity;

public sealed record TwoFactorStatus(bool Enabled, DateTimeOffset? EnabledAt, int RecoveryCodesLeft);

/// <summary>What the person scans or types into their authenticator app while setting up.</summary>
public sealed record TwoFactorSetup(string Secret, string Uri, string QrSvg);

/// <summary>What an operator's reset found: no such account, two-factor sign-in already off, or done.</summary>
public enum TwoFactorReset { NoAccount, AlreadyOff, Done }

/// <summary>
/// Optional two-factor sign-in for accounts (docs/platform.md#two-factor-sign-in): a code from an
/// authenticator app after the password, or a one-time recovery code. The app's secret is encrypted
/// with the secrets master key; recovery codes and sign-in challenges are stored only as hashes.
/// </summary>
public sealed class TwoFactorService(
    IDbContextFactory<AgentDbContext> dbFactory,
    SecretProtector protector,
    IdentityService identity,
    PlatformMailer mailer,
    TimeProvider time,
    ILogger<TwoFactorService> logger)
{
    public const string Issuer = "Testigo";
    public const int RecoveryCodeCount = 10;
    /// <summary>Wrong codes one sign-in may try before the password has to be entered again.</summary>
    public const int MaxChallengeAttempts = 5;
    /// <summary>Wrong codes one account may get across all its sign-ins within
    /// <see cref="WrongCodeWindow"/>; after that, signing in waits. Someone who has the password
    /// can't just start sign-in after sign-in to keep guessing.</summary>
    public const int MaxWrongCodesPerWindow = 10;
    public static readonly TimeSpan WrongCodeWindow = TimeSpan.FromMinutes(15);

    private const string ProtectorScope = "user-totp";
    private const string RecoveryAlphabet = "abcdefghjkmnpqrstuvwxyz23456789"; // no 0/o, 1/l/i

    public async Task<TwoFactorStatus> StatusAsync(string userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        return user is null ? new TwoFactorStatus(false, null, 0) : new TwoFactorStatus(user.TwoFactorEnabledAt is not null, user.TwoFactorEnabledAt, RecoveryHashes(user).Count);
    }

    /// <summary>Starts setting up: a new secret for the app, kept pending until a code confirms it.
    /// Asks for the password, so an unattended signed-in browser can't tie the account to someone
    /// else's phone.</summary>
    public async Task<IdentityResult<TwoFactorSetup>> BeginSetupAsync(string userId, string password, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null || !PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash)) return IdentityResult<TwoFactorSetup>.Fail("The password is wrong.");
        if (user.TwoFactorEnabledAt is not null) return IdentityResult<TwoFactorSetup>.Fail("Two-factor sign-in is already on. Turn it off first to use another app.");

        var secret = Totp.NewSecret();
        user.PendingTotpSecret = Protect(user.UserId, secret);
        await db.SaveChangesAsync(ct);

        var uri = Totp.Uri(Issuer, user.Email, secret);
        return IdentityResult<TwoFactorSetup>.Ok(new TwoFactorSetup(Totp.ToBase32(secret), uri, QrSvg(uri)));
    }

    /// <summary>Turns two-factor sign-in on once the app's first code checks out. Returns the
    /// recovery codes, shown this once. Every other browser has to sign in again.</summary>
    public async Task<IdentityResult<IReadOnlyList<string>>> EnableAsync(string userId, string code, string keepSessionHash, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return IdentityResult<IReadOnlyList<string>>.Fail("Sign in again.");
        if (user.TwoFactorEnabledAt is not null) return IdentityResult<IReadOnlyList<string>>.Fail("Two-factor sign-in is already on.");
        if (user.PendingTotpSecret is null) return IdentityResult<IReadOnlyList<string>>.Fail("Start the setup again.");

        var secret = Unprotect(user.UserId, user.PendingTotpSecret);
        if (Totp.Verify(secret, Normalize(code), time.GetUtcNow(), lastUsedStep: 0) is not { } step)
        {
            return IdentityResult<IReadOnlyList<string>>.Fail("That code isn't right. Use the newest code from the app, and check the time on your phone is set automatically. If you started setting up more than once, scan the newest QR code.");
        }

        var codes = NewRecoveryCodes(user);
        user.TotpSecret = user.PendingTotpSecret;
        user.PendingTotpSecret = null;
        user.TotpLastStep = step;
        user.TwoFactorEnabledAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await db.Sessions.Where(s => s.UserId == userId && s.TokenHash != keepSessionHash).ExecuteDeleteAsync(ct);

        logger.LogInformation("Two-factor sign-in turned on for {UserId}", userId);
        Notify(user.Email, "Two-factor sign-in is on for your Testigo account",
            "Two-factor sign-in was just turned on for your Testigo account. From now on, signing in asks for a code from your authenticator app.\n\n" +
            "If this wasn't you, reset your password straight away and contact the operator of your Testigo server.");
        return IdentityResult<IReadOnlyList<string>>.Ok(codes);
    }

    /// <summary>Turns two-factor sign-in off. Needs the password and a current code (or a recovery code).</summary>
    public async Task<IdentityResult<bool>> DisableAsync(string userId, string password, string code, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null || !PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash)) return IdentityResult<bool>.Fail("The password is wrong.");
        if (user.TwoFactorEnabledAt is null) return IdentityResult<bool>.Ok(true);
        switch (await UseSecondFactorAsync(db, user, code, time.GetUtcNow(), ct))
        {
            case SecondFactor.Wrong: return IdentityResult<bool>.Fail("That code isn't right.");
            case SecondFactor.AlreadyUsed: return IdentityResult<bool>.Fail(AlreadyUsedMessage);
        }


        Clear(user);
        await db.SaveChangesAsync(ct);
        await db.LoginChallenges.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        logger.LogInformation("Two-factor sign-in turned off for {UserId}", userId);
        Notify(user.Email, "Two-factor sign-in was turned off on your Testigo account",
            "Two-factor sign-in was just turned off for your Testigo account. Signing in now needs only your password.\n\n" +
            "If this wasn't you, reset your password straight away, turn two-factor sign-in back on, and contact the operator of your Testigo server.");
        return IdentityResult<bool>.Ok(true);
    }

    /// <summary>A new set of recovery codes, replacing the old ones. Needs the password and a current code.</summary>
    public async Task<IdentityResult<IReadOnlyList<string>>> RegenerateRecoveryCodesAsync(string userId, string password, string code, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null || !PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash)) return IdentityResult<IReadOnlyList<string>>.Fail("The password is wrong.");
        if (user.TwoFactorEnabledAt is null) return IdentityResult<IReadOnlyList<string>>.Fail("Two-factor sign-in is off.");
        switch (await UseSecondFactorAsync(db, user, code, time.GetUtcNow(), ct))
        {
            case SecondFactor.Wrong: return IdentityResult<IReadOnlyList<string>>.Fail("That code isn't right.");
            case SecondFactor.AlreadyUsed: return IdentityResult<IReadOnlyList<string>>.Fail(AlreadyUsedMessage);
        }


        var codes = NewRecoveryCodes(user);
        await db.SaveChangesAsync(ct);
        return IdentityResult<IReadOnlyList<string>>.Ok(codes);
    }

    /// <summary>The second step of signing in: the challenge from a right password, and a code.
    /// A few wrong codes end the challenge, so codes can't be guessed.</summary>
    public async Task<IdentityResult<SignedIn>> CompleteSignInAsync(string challenge, string code, CancellationToken ct)
    {
        const string expired = "This sign-in has expired. Enter your password again.";
        const string tooMany = "Too many wrong codes. Enter your password again.";
        if (string.IsNullOrEmpty(challenge) || !challenge.StartsWith("c_", StringComparison.Ordinal)) return IdentityResult<SignedIn>.Fail(expired);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = time.GetUtcNow();
        var hash = SecretTokens.Hash(challenge);
        // Count the attempt before checking the code, in one statement, so requests sent at the
        // same moment can't try more codes than the limit between them.
        var counted = await db.LoginChallenges
            .Where(c => c.TokenHash == hash && c.ExpiresAt > now && c.Attempts < MaxChallengeAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), ct);
        var pending = await db.LoginChallenges.AsNoTracking().FirstOrDefaultAsync(c => c.TokenHash == hash, ct);
        if (counted == 0 || pending is null)
        {
            return IdentityResult<SignedIn>.Fail(pending is not null && pending.ExpiresAt > now ? tooMany : expired);
        }

        var user = await db.Users.FindAsync([pending.UserId], ct);
        if (user is null || user.TwoFactorEnabledAt is null)
        {
            await db.LoginChallenges.Where(c => c.TokenHash == hash).ExecuteDeleteAsync(ct);
            return IdentityResult<SignedIn>.Fail(expired);
        }

        var used = await UseSecondFactorAsync(db, user, code, now, ct);
        if (used == SecondFactor.AlreadyUsed)
        {
            // A right code, only spent: not a guess, so it doesn't count against the limits.
            await db.LoginChallenges.Where(c => c.TokenHash == hash && c.Attempts > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts - 1), ct);
            return IdentityResult<SignedIn>.Fail(AlreadyUsedMessage);
        }

        if (used == SecondFactor.Wrong)
        {
            // Wrong codes stay counted on the challenge (kept for WrongCodeWindow), which is what
            // makes signing in wait after MaxWrongCodesPerWindow of them.
            if (pending.Attempts >= MaxChallengeAttempts)
            {
                logger.LogWarning("Too many wrong two-factor codes for {UserId}; the sign-in was ended", user.UserId);
                return IdentityResult<SignedIn>.Fail(tooMany);
            }

            return IdentityResult<SignedIn>.Fail("That code isn't right.");
        }

        // One session per challenge, even if two right codes arrive together.
        if (await db.LoginChallenges.Where(c => c.TokenHash == hash).ExecuteDeleteAsync(ct) == 0) return IdentityResult<SignedIn>.Fail(expired);
        await db.LoginChallenges.Where(c => c.UserId == user.UserId && c.CreatedAt <= now - WrongCodeWindow).ExecuteDeleteAsync(ct);
        if (await db.Memberships.FindAsync([pending.TenantId, user.UserId], ct) is null || await IdentityService.IsSuspendedAsync(db, pending.TenantId, ct))
        {
            return IdentityResult<SignedIn>.Fail(expired);
        }

        user.LastLoginAt = now;
        var token = identity.NewSession(db, user.UserId, pending.TenantId, now);
        await db.SaveChangesAsync(ct);

        if (used == SecondFactor.RecoveryCode)
        {
            var left = RecoveryHashes(user).Count;
            Notify(user.Email, "A recovery code was used to sign in to your Testigo account",
                $"Someone signed in to your Testigo account with one of your recovery codes. You have {left} left.\n\n" +
                "If this was you, consider making new codes under Settings → Account. If it wasn't, reset your password straight away.");
        }

        return IdentityResult<SignedIn>.Ok(new SignedIn(token, user, pending.TenantId));
    }

    /// <summary>Wrong codes this account got within <see cref="WrongCodeWindow"/>, across all its
    /// sign-ins. Signing in waits once it reaches <see cref="MaxWrongCodesPerWindow"/>.</summary>
    internal static async Task<bool> TooManyWrongCodesAsync(AgentDbContext db, string userId, DateTimeOffset now, CancellationToken ct)
    {
        var since = now - WrongCodeWindow;
        var wrong = await db.LoginChallenges.Where(c => c.UserId == userId && c.CreatedAt > since).SumAsync(c => c.Attempts, ct);
        return wrong >= MaxWrongCodesPerWindow;
    }

    /// <summary>For an operator, when someone has lost their phone and their recovery codes: turns
    /// two-factor sign-in off and signs out every browser.</summary>
    public async Task<TwoFactorReset> ResetAsync(string email, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalized = IdentityService.NormalizeEmail(email ?? string.Empty);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null) return TwoFactorReset.NoAccount;
        // Nothing to reset: don't sign them out or tell them it was reset.
        if (user.TwoFactorEnabledAt is null) return TwoFactorReset.AlreadyOff;
        Clear(user);
        await db.SaveChangesAsync(ct);
        await db.Sessions.Where(s => s.UserId == user.UserId).ExecuteDeleteAsync(ct);
        await db.LoginChallenges.Where(c => c.UserId == user.UserId).ExecuteDeleteAsync(ct);
        logger.LogWarning("An operator turned off two-factor sign-in for {UserId}", user.UserId);
        Notify(user.Email, "Two-factor sign-in was reset on your Testigo account",
            "The operator of your Testigo server turned off two-factor sign-in for your account, and signed out every browser. " +
            "Sign in with your password, then turn it back on under Settings → Account.");
        return TwoFactorReset.Done;
    }

    private enum SecondFactor { Wrong, AlreadyUsed, AppCode, RecoveryCode }

    private const string AlreadyUsedMessage = "That code was just used. Wait for the next code in your app.";

    /// <summary>A current authenticator code, or an unused recovery code, used up in one conditional
    /// update: of two requests carrying the same code at the same moment, only one gets through.
    /// The tracked user is updated to match, as if saved (current and original value), so a later
    /// save doesn't write it again over a newer value from another request.</summary>
    private async Task<SecondFactor> UseSecondFactorAsync(AgentDbContext db, UserRecord user, string? code, DateTimeOffset now, CancellationToken ct)
    {
        var clean = Normalize(code);
        if (clean.Length == Totp.Digits && clean.All(char.IsAsciiDigit))
        {
            if (user.TotpSecret is null) return SecondFactor.Wrong;
            // Checked without the last-used step first, so a right code that was already used (to
            // turn this on, or by another sign-in) gets its own answer rather than "isn't right".
            if (Totp.Verify(Unprotect(user.UserId, user.TotpSecret), clean, now, lastUsedStep: 0) is not { } step) return SecondFactor.Wrong;
            if (step <= user.TotpLastStep) return SecondFactor.AlreadyUsed;
            var claimed = await db.Users.Where(u => u.UserId == user.UserId && u.TotpLastStep < step)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TotpLastStep, step), ct);
            if (claimed == 0) return SecondFactor.AlreadyUsed;
            user.TotpLastStep = step;
            db.Entry(user).Property(u => u.TotpLastStep).OriginalValue = step;
            return SecondFactor.AppCode;
        }

        var before = user.RecoveryCodeHashes;
        var hashes = RecoveryHashes(user);
        var match = hashes.FirstOrDefault(h => SecretTokens.HashEquals(clean, h));
        if (match is null) return SecondFactor.Wrong;
        hashes.Remove(match);
        var after = string.Join(' ', hashes);
        var removed = await db.Users.Where(u => u.UserId == user.UserId && u.RecoveryCodeHashes == before)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.RecoveryCodeHashes, after), ct);
        if (removed == 0) return SecondFactor.Wrong;
        user.RecoveryCodeHashes = after;
        db.Entry(user).Property(u => u.RecoveryCodeHashes).OriginalValue = after;
        return SecondFactor.RecoveryCode;
    }

    private static IReadOnlyList<string> NewRecoveryCodes(UserRecord user)
    {
        var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToList();
        user.RecoveryCodeHashes = string.Join(' ', codes.Select(c => SecretTokens.Hash(Normalize(c))));
        return codes;
    }

    private static string NewRecoveryCode()
    {
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++) chars[i] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
        return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
    }

    private static List<string> RecoveryHashes(UserRecord user) =>
        user.RecoveryCodeHashes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Codes are typed with spaces or dashes ("123 456", "abcde-fghjk"); neither matters.</summary>
    private static string Normalize(string? code) =>
        new((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').Select(char.ToLowerInvariant).ToArray());

    private static void Clear(UserRecord user)
    {
        user.TwoFactorEnabledAt = null;
        user.TotpSecret = null;
        user.PendingTotpSecret = null;
        user.TotpLastStep = 0;
        user.RecoveryCodeHashes = string.Empty;
    }

    private byte[] Protect(string userId, byte[] secret) => protector.Protect(Totp.ToBase32(secret), ProtectorScope, userId);

    private byte[] Unprotect(string userId, byte[] stored) => Totp.FromBase32(protector.Unprotect(stored, ProtectorScope, userId));

    private static string QrSvg(string uri)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        using var svg = new SvgQRCode(data);
        return svg.GetGraphic(5);
    }

    /// <summary>Security notices go out in the background: a slow mail server mustn't hold up the change.</summary>
    private void Notify(string to, string subject, string body)
    {
        if (!mailer.Enabled) return;
        var email = PlatformEmail.FromText(subject, body) with
        {
            Footnote = $"You're getting this because it's about the security of your {PlatformEmailTemplate.ProductName} account."
        };
        _ = Task.Run(() => mailer.SendAsync(to, email, CancellationToken.None));
    }
}
