namespace AgentRuntime.Api.Platform;

/// <summary>
/// The endpoints the reporter app calls on a reporter's behalf. Requests to them are never logged,
/// traced or rate-limited by address, so no reporter IP or user agent is recorded (NFR-SEC-3).
/// </summary>
public static class ReporterPaths
{
    public const string Intake = "/api/intake";

    public static bool IsReporterFacing(PathString path) => path.StartsWithSegments(Intake);
}
