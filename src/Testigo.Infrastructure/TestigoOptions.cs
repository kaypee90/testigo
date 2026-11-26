namespace Testigo.Infrastructure;

/// <summary>Testigo's settings (section "Testigo"). Things to agree with the deploying organization are settings, not code.</summary>
public sealed class TestigoOptions
{
    public const string Section = "Testigo";
    /// <summary>Opt-in local endpoint enforcement (including compatible APIs). Mock remains the default provider.</summary>
    public bool LocalModelsOnly { get; set; }

    public KeyOptions Keys { get; set; } = new();
    public IntakeOptions Intake { get; set; } = new();
    public StaffOptions Staff { get; set; } = new();
    public WorkerOptions Worker { get; set; } = new();
    public DemoOptions Demo { get; set; } = new();
}

/// <summary>
/// Where the master keys come from (NFR-SEC-4): configuration values, or a key file kept outside
/// the database (a mounted secret or volume). The database only ever holds wrapped data keys.
/// </summary>
public sealed class KeyOptions
{
    /// <summary>A JSON key file: {"active_master_key": "mk1", "master_keys": {"mk1": "base64"}, "lookup_key": "base64"}.</summary>
    public string? KeyFile { get; set; }
    /// <summary>Creates the key file with fresh random keys when it doesn't exist. For a local demo only.</summary>
    public bool GenerateIfMissing { get; set; }
    /// <summary>Master keys by id, base64 (32 bytes each). Used instead of the key file when set.</summary>
    public Dictionary<string, string> MasterKeys { get; set; } = [];
    public string? ActiveMasterKey { get; set; }
    /// <summary>The HMAC key for case-code lookups, base64 (32 bytes).</summary>
    public string? LookupKey { get; set; }
}

public sealed class IntakeOptions
{
    /// <summary>The organization public reports go to. VALIDATE: one intake per organization later.</summary>
    public string Organization { get; set; } = AgentRuntime.Tenancy.TenantIds.Default;
    /// <summary>The key the reporter app sends in X-Testigo-Intake-Key. Intake is closed while neither this nor the file is set.</summary>
    public string? SharedKey { get; set; }
    public string? SharedKeyFile { get; set; }
    /// <summary>Writes a random shared key to SharedKeyFile when it doesn't exist. For a local demo only.</summary>
    public bool GenerateSharedKeyIfMissing { get; set; }
    /// <summary>Submissions accepted per minute across the whole intake (the reporter app also limits per client).</summary>
    public int SubmissionsPerMinute { get; set; } = 30;
}

public sealed class StaffOptions
{
    /// <summary>Staff must have two-factor sign-in on to read cases (NFR-SEC-8).</summary>
    public bool RequireTwoFactor { get; set; } = true;
    /// <summary>Marking a case verified needs the Reviewer role (FR-CASE-4).</summary>
    public bool ReviewerVerifies { get; set; } = true;
    /// <summary>How many recent reports the related-report search compares against.</summary>
    public int RelatedWindow { get; set; } = 500;
}

public sealed class WorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 3;
    /// <summary>Reports analyzed per poll. Each gets one attempt (NFR-AI-5).</summary>
    public int BatchSize { get; set; } = 4;
}

public sealed class DemoOptions
{
    /// <summary>Loads the synthetic demo reports into the intake organization when it has none.</summary>
    public bool Seed { get; set; }
    /// <summary>Gives the local admin account the Analyst and Reviewer roles. For a local demo only.</summary>
    public bool GrantLocalAdminRoles { get; set; }
}
