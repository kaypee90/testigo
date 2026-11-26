namespace AgentRuntime.Infrastructure.Persistence;

/// <summary>One audit log record (see Safety/SafetyContracts). Append-only; hash-chained per scope.</summary>
public sealed class AuditRecord
{
    public long Id { get; set; }
    public required string Scope { get; set; }
    public long Seq { get; set; }
    public required string Key { get; set; }
    public DateTimeOffset At { get; set; }
    public required string ActorType { get; set; }
    public required string ActorId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public required string Action { get; set; }
    public string Target { get; set; } = string.Empty;
    public string? SideEffects { get; set; }
    public string Outcome { get; set; } = "ok";
    public string Summary { get; set; } = string.Empty;
    public string DetailJson { get; set; } = "{}";
    public required string PreviousHash { get; set; }
    public required string Hash { get; set; }
}

/// <summary>The latest record of each audit chain, locked while appending so records are sequenced one at a time.</summary>
public sealed class AuditHeadRecord
{
    public required string Scope { get; set; }
    public long Seq { get; set; }
    public required string Hash { get; set; }
}

// ---- Platform: organizations, people, access, billing (docs/platform.md) ----

/// <summary>An organization: the unit of isolation and membership.</summary>
public sealed class TenantRecord
{
    public required string TenantId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>"Active" or "Suspended". A platform operator suspends an organization; its members
    /// can't sign in to it until it's reactivated.</summary>
    public string Status { get; set; } = TenantStatuses.Active;
    public DateTimeOffset? SuspendedAt { get; set; }
    public string? SuspendedReason { get; set; }
}

public static class TenantStatuses
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
}

public sealed class UserRecord
{
    public required string UserId { get; set; }
    /// <summary>Lower-cased and trimmed; unique.</summary>
    public required string Email { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>PBKDF2 (see Identity/PasswordHasher); never the password.</summary>
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>When two-factor sign-in was turned on; null while it's off (Identity/TwoFactorService).</summary>
    public DateTimeOffset? TwoFactorEnabledAt { get; set; }
    /// <summary>The authenticator app's secret, encrypted with the secrets master key; set while
    /// two-factor sign-in is on.</summary>
    public byte[]? TotpSecret { get; set; }
    /// <summary>A secret being set up, encrypted, until a first code from the app confirms it.</summary>
    public byte[]? PendingTotpSecret { get; set; }
    /// <summary>The last 30-second step a code was accepted for, so the same code can't be used twice.</summary>
    public long TotpLastStep { get; set; }
    /// <summary>SHA-256 hashes of the unused recovery codes, space-separated; each works once.</summary>
    public string RecoveryCodeHashes { get; set; } = string.Empty;
}

/// <summary>A password that was right for an account with two-factor sign-in, waiting for its code.
/// Only a hash of the token handed to the browser is stored; it expires in minutes and allows a few
/// wrong codes.</summary>
public sealed class LoginChallengeRecord
{
    public required string TokenHash { get; set; }
    public required string UserId { get; set; }
    /// <summary>The organization the session will open in, chosen when the password was checked.</summary>
    public required string TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
}

/// <summary>
/// A sign-up waiting for its email to be confirmed (docs/platform.md#email-verification). The account
/// and its organization are only created when the link sent to <see cref="Email"/> is opened, so an
/// address nobody confirmed never holds an account. Only a hash of the link's token is stored, and the
/// password only as its hash. Each sign-up keeps its own link and password, so someone else signing
/// up with the same address can't replace the link its owner is waiting for.
/// </summary>
public sealed class PendingSignUpRecord
{
    /// <summary>Handed to the browser that signed up, to ask for its link again; it isn't the link.</summary>
    public required string Id { get; set; }
    public required string TokenHash { get; set; }
    /// <summary>Lower-cased and trimmed.</summary>
    public required string Email { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? OrganizationName { get; set; }
    public required string PasswordHash { get; set; }
    /// <summary>Where the sign-up came from (its /64 for IPv6), counted against the per-address limit when it completes.</summary>
    public string? ClientAddress { get; set; }
    /// <summary>The invitation this sign-up joins, checked again when the link is opened; null for a new organization.</summary>
    public string? InvitationId { get; set; }
    /// <summary>Wrong passwords entered with this link; it stops working after a few.</summary>
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>When the link was last emailed, so resending can't be used to flood an inbox.</summary>
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class MembershipRecord
{
    public required string TenantId { get; set; }
    public required string UserId { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A signed-in browser. Only a hash of the cookie's token is stored.</summary>
public sealed class SessionRecord
{
    public required string TokenHash { get; set; }
    public required string UserId { get; set; }
    /// <summary>The organization the session is currently working in.</summary>
    public required string TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class InvitationRecord
{
    public required string InvitationId { get; set; }
    public required string TenantId { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public required string TokenHash { get; set; }
    public string InvitedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
