namespace Testigo.Cases;

/// <summary>Where a case is in its workflow (FR-CASE-3). Stored by name.</summary>
public enum CaseStatus
{
    Received,
    InTriage,
    NeedsInfo,
    Verified,
    Referred,
    InLitigation,
    ClosedNoAction,
    Withdrawn
}

/// <summary>
/// The explicit, validated status transitions of a case (FR-CASE-3), like the runtime's agent
/// status transitions. Every allowed move is listed here; anything else is refused.
/// </summary>
public static class CaseStatusTransitions
{
    private static readonly IReadOnlyDictionary<CaseStatus, CaseStatus[]> Allowed = new Dictionary<CaseStatus, CaseStatus[]>
    {
        [CaseStatus.Received] = [CaseStatus.InTriage, CaseStatus.ClosedNoAction, CaseStatus.Withdrawn],
        [CaseStatus.InTriage] = [CaseStatus.NeedsInfo, CaseStatus.Verified, CaseStatus.ClosedNoAction, CaseStatus.Withdrawn],
        [CaseStatus.NeedsInfo] = [CaseStatus.InTriage, CaseStatus.ClosedNoAction, CaseStatus.Withdrawn],
        [CaseStatus.Verified] = [CaseStatus.Referred, CaseStatus.InLitigation, CaseStatus.ClosedNoAction, CaseStatus.Withdrawn],
        [CaseStatus.Referred] = [CaseStatus.InLitigation, CaseStatus.ClosedNoAction, CaseStatus.Withdrawn],
        [CaseStatus.InLitigation] = [CaseStatus.ClosedNoAction, CaseStatus.Withdrawn],
        // A closed case can be reopened for triage; a withdrawn one can't come back.
        [CaseStatus.ClosedNoAction] = [CaseStatus.InTriage],
        [CaseStatus.Withdrawn] = [],
    };

    /// <summary>The statuses a case in <paramref name="from"/> may move to.</summary>
    public static IReadOnlyList<CaseStatus> From(CaseStatus from) => Allowed[from];

    public static bool CanMove(CaseStatus from, CaseStatus to) => Allowed[from].Contains(to);

    /// <summary>Moving to Verified needs a Reviewer by default (FR-CASE-4).</summary>
    public static bool NeedsReviewer(CaseStatus to) => to == CaseStatus.Verified;

    /// <summary>The wire name used by the API and the database: snake_case, as in the requirements.</summary>
    public static string Name(CaseStatus status) => status switch
    {
        CaseStatus.Received => "received",
        CaseStatus.InTriage => "in_triage",
        CaseStatus.NeedsInfo => "needs_info",
        CaseStatus.Verified => "verified",
        CaseStatus.Referred => "referred",
        CaseStatus.InLitigation => "in_litigation",
        CaseStatus.ClosedNoAction => "closed_no_action",
        CaseStatus.Withdrawn => "withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParse(string? name, out CaseStatus status)
    {
        foreach (var s in Enum.GetValues<CaseStatus>())
        {
            if (string.Equals(Name(s), name, StringComparison.Ordinal))
            {
                status = s;
                return true;
            }
        }

        status = default;
        return false;
    }
}

/// <summary>Where the automatic first pass is for a report (FR-ANA-1, NFR-AI-5).</summary>
public enum AnalysisState
{
    /// <summary>Waiting for the triage worker.</summary>
    Pending,
    Running,
    Complete,
    /// <summary>The budget ran out or the model failed: a person takes it from here. Never retried in a loop.</summary>
    Incomplete
}

/// <summary>Analyst-confirmed severity (requirements §4.1, dimension 7).</summary>
public enum Severity
{
    Unset,
    Low,
    Medium,
    High,
    Critical
}
