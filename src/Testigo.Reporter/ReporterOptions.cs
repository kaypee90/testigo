namespace Testigo.Reporter;

/// <summary>The reporter app's settings (section "Reporter").</summary>
public sealed class ReporterOptions
{
    public const string Section = "Reporter";

    /// <summary>Where the API's intake endpoints are, on the internal network.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5180";
    /// <summary>The intake shared key, or the file it's in (written by the API in the demo).</summary>
    public string? IntakeKey { get; set; }
    public string? IntakeKeyFile { get; set; }
    /// <summary>The neutral page the quick-exit control opens (FR-INT-5). VALIDATE with the deploying organization.</summary>
    public string QuickExitUrl { get; set; } = "https://www.google.com/search?q=pron%C3%B3stico+del+tiempo";
    /// <summary>Submissions per client in <see cref="SubmissionWindowMinutes"/>, counted in memory under a rotating salt.</summary>
    public int SubmissionsPerClient { get; set; } = 5;
    public int SubmissionWindowMinutes { get; set; } = 10;
    /// <summary>
    /// The languages offered to reporters (FR-INT-8). Spanish is always first and the default;
    /// empty offers every language with a texts file. VALIDATE: a language goes live only once a
    /// person has reviewed its texts (docs/open-questions.md).
    /// </summary>
    public string[]? Languages { get; set; }
    /// <summary>Behind a reverse proxy (Caddy): take the client address from X-Forwarded-For, only for rate limiting.</summary>
    public bool BehindProxy { get; set; }
}
