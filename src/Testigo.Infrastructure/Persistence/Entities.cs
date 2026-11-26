namespace Testigo.Infrastructure.Persistence;

// The data model of requirements §7.5. Every table carries organization_id. Fields ending in
// "Cipher" are encrypted with IFieldCipher (NFR-SEC-4): report content under the organization's
// Content key, contact details under its separate Contact key. Reads, status changes and
// decisions also go to the hash-chained audit log; these tables hold the current state.

/// <summary>An organization's wrapped data key (NFR-SEC-4).</summary>
public sealed class DataKeyRow
{
    public required string KeyId { get; set; }
    public required string OrganizationId { get; set; }
    public required string Purpose { get; set; }
    public required byte[] WrappedKey { get; set; }
    public required string MasterKeyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}

/// <summary>A report as submitted. The reporter's text and answers are ciphertext only.</summary>
public sealed class ReportRow
{
    public Guid Id { get; set; }
    public required string OrganizationId { get; set; }
    /// <summary>HMAC of the normalized case code; the code itself is never stored (FR-INT-6).</summary>
    public required string CaseCodeHash { get; set; }
    /// <summary>Argon2id hash of the reporter's passphrase (NFR-SEC-9).</summary>
    public required string PassphraseHash { get; set; }
    public required string FormVersion { get; set; }
    public required string NoticeVersion { get; set; }
    /// <summary>"public" or "assisted".</summary>
    public required string Channel { get; set; }
    public string Language { get; set; } = "und";
    /// <summary>Kept in clear for the queue filter: a country is not identifying on its own.</summary>
    public string? Country { get; set; }
    public required byte[] AnswersCipher { get; set; }
    public required byte[] FreeTextCipher { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }

    public string AnalysisState { get; set; } = "pending";
    public DateTimeOffset? AnalysisStartedAt { get; set; }
    public DateTimeOffset? AnalysisFinishedAt { get; set; }
    /// <summary>Why the automatic pass didn't finish (no report content).</summary>
    public string? AnalysisNote { get; set; }
}

/// <summary>Contact details, only when the reporter consented to be contacted.</summary>
public sealed class ContactRow
{
    public Guid ReportId { get; set; }
    public required string OrganizationId { get; set; }
    public required byte[] MethodCipher { get; set; }
    public required byte[] ValueCipher { get; set; }
}

/// <summary>What the reporter agreed to, against which version of the notice.</summary>
public sealed class ConsentRow
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public required string OrganizationId { get; set; }
    public required string NoticeVersion { get; set; }
    public bool Processing { get; set; }
    public bool Contact { get; set; }
    public bool Publication { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
}

/// <summary>A case: one report in Phase 0.</summary>
public sealed class CaseRow
{
    public Guid Id { get; set; }
    public required string OrganizationId { get; set; }
    public Guid ReportId { get; set; }
    /// <summary>A short, per-organization number staff use to talk about the case ("T-0042"). Not the case code.</summary>
    public int Number { get; set; }
    public string Status { get; set; } = "received";
    public bool ImmediateDanger { get; set; }
    /// <summary>"rules", "model", "rules+model" or null.</summary>
    public string? DangerSource { get; set; }
    public string Severity { get; set; } = "unset";
    public string? AssigneeId { get; set; }
    public required string TaxonomyVersion { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Optimistic concurrency: two people changing a case at once don't overwrite each other.</summary>
    public uint Version { get; set; }
}

/// <summary>Every status change, with who, when and why (FR-CASE-3).</summary>
public sealed class StatusChangeRow
{
    public Guid Id { get; set; }
    public Guid CaseId { get; set; }
    public required string OrganizationId { get; set; }
    public required string From { get; set; }
    public required string To { get; set; }
    public required string ByUserId { get; set; }
    public DateTimeOffset At { get; set; }
    public required byte[] ReasonCipher { get; set; }
}

/// <summary>A suggested or confirmed classification term or extracted field, with its evidence.</summary>
public sealed class ClassificationRow
{
    public Guid Id { get; set; }
    public Guid CaseId { get; set; }
    public required string OrganizationId { get; set; }
    /// <summary>"term" (dimension + term), "field" (field + value) or "danger".</summary>
    public required string Kind { get; set; }
    public required string Dimension { get; set; }
    public string? Term { get; set; }
    /// <summary>An extracted field's value, encrypted.</summary>
    public byte[]? ValueCipher { get; set; }
    public byte[]? QuoteCipher { get; set; }
    public int? EvidenceStart { get; set; }
    public int? EvidenceLength { get; set; }
    /// <summary>"ai" or "analyst".</summary>
    public required string Source { get; set; }
    /// <summary>"suggested", "confirmed" or "rejected".</summary>
    public string State { get; set; } = "suggested";
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public required string TaxonomyVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>The redacted copy agents and search work on, and the encrypted map back to the original.</summary>
public sealed class RedactedCopyRow
{
    public Guid ReportId { get; set; }
    public required string OrganizationId { get; set; }
    public required byte[] TextCipher { get; set; }
    public required byte[] MapCipher { get; set; }
    public required string MethodVersion { get; set; }
    /// <summary>The kinds of personal data found, e.g. "PERSON,RUT".</summary>
    public string Types { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A possible relation between two cases (FR-ANA-5). Suggested by the system, decided by a person.</summary>
public sealed class CaseLinkRow
{
    public Guid Id { get; set; }
    public required string OrganizationId { get; set; }
    public Guid CaseId { get; set; }
    public Guid OtherCaseId { get; set; }
    public required string Kind { get; set; }
    public double Score { get; set; }
    public string SuggestedBy { get; set; } = "system";
    public string State { get; set; } = "suggested";
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Cached model answers, encrypted (they quote the redacted report).</summary>
public sealed class TriageCacheRow
{
    public required string OrganizationId { get; set; }
    public required string CacheKey { get; set; }
    public required byte[] ArgumentsCipher { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A staff member's Testigo role in an organization (NFR-SEC-8).</summary>
public sealed class StaffRoleRow
{
    public required string OrganizationId { get; set; }
    public required string UserId { get; set; }
    /// <summary>"analyst", "reviewer" or "intake_advocate".</summary>
    public required string Role { get; set; }
    public required string GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}

/// <summary>The next case number per organization.</summary>
public sealed class CaseCounterRow
{
    public required string OrganizationId { get; set; }
    public int Next { get; set; } = 1;
}
