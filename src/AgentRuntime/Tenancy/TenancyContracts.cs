using System.Security.Cryptography;

namespace AgentRuntime.Tenancy;

/// <summary>
/// Tenant (organization) ids. Every account membership, case and audit record belongs to exactly one
/// organization.
/// </summary>
public static class TenantIds
{
    /// <summary>The tenant of single-user installs and of everything created before multi-tenancy.</summary>
    public const string Default = "default";

    public static string New() => "t-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

    /// <summary>Records written before tenancy existed have no tenant: they belong to the default one.</summary>
    public static string Normalize(string? tenantId) => string.IsNullOrWhiteSpace(tenantId) ? Default : tenantId;

    public static bool Same(string? a, string? b) => Normalize(a) == Normalize(b);
}

/// <summary>A member's role in an organization. Ordered: each role can do everything the ones
/// before it can.</summary>
public enum TenantRole
{
    Viewer = 0,
    Member = 1,
    Admin = 2,
    Owner = 3
}
