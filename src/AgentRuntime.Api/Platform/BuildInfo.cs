using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AgentRuntime.Api.Platform;

/// <summary>
/// Which build of the server is running, for operators (docs/platform.md#which-version-is-running).
/// The deploy stamps the image with the git commit and build time (<c>App:Version</c>,
/// <c>App:BuiltAt</c>). A local build from a git checkout falls back to the
/// commit the .NET SDK records in the assembly; a build with neither has no version.
/// </summary>
public sealed partial class BuildInfo
{
    public BuildInfo(IConfiguration configuration)
    {
        var stamped = configuration["App:Version"]?.Trim();
        Commit = string.IsNullOrEmpty(stamped) ? FromAssembly() : stamped;
        if (Commit is not null && IsCommitHash(Commit)) ShortCommit = Commit[..Math.Min(7, Commit.Length)];
        else ShortCommit = Commit;

        BuiltAt = DateTimeOffset.TryParse(configuration["App:BuiltAt"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var builtAt) ? builtAt : null;

        using var process = Process.GetCurrentProcess();
        StartedAt = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
    }

    /// <summary>The full commit (or whatever version the deploy stamped); null when unknown.</summary>
    public string? Commit { get; }

    /// <summary>The first seven characters of a commit hash, as GitHub shows it.</summary>
    public string? ShortCommit { get; }

    public DateTimeOffset? BuiltAt { get; }

    /// <summary>When this server process started.</summary>
    public DateTimeOffset StartedAt { get; }

    private static string? FromAssembly()
    {
        // "1.0.0+<commit>" when built from a git checkout; just "1.0.0" otherwise.
        var informational = typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = informational?.IndexOf('+') ?? -1;
        return plus >= 0 && IsCommitHash(informational![(plus + 1)..]) ? informational[(plus + 1)..] : null;
    }

    private static bool IsCommitHash(string value) => CommitHash().IsMatch(value);

    [GeneratedRegex("^[0-9a-f]{7,64}$")]
    private static partial Regex CommitHash();
}
