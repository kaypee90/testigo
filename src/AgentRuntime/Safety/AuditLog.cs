using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentRuntime.Safety;

/// <summary>One audited action. Records are append-only and hash-chained per scope.</summary>
public sealed record AuditEntry
{
    /// <summary>The workspace (or task) the action belongs to.</summary>
    public required string Scope { get; init; }
    /// <summary>Deduplication key: an action replayed after a crash is recorded once.</summary>
    public required string Key { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>"agent", "user", "system" or "watch".</summary>
    public required string ActorType { get; init; }
    public required string ActorId { get; init; }
    public string ActorName { get; init; } = string.Empty;
    /// <summary>e.g. tool.call, tool.denied, approval.requested, approval.approved, connection.added.</summary>
    public required string Action { get; init; }
    public string Target { get; init; } = string.Empty;
    public string? SideEffects { get; init; }
    /// <summary>"ok", "failed", "denied", "pending", "unknown".</summary>
    public string Outcome { get; init; } = "ok";
    public string Summary { get; init; } = string.Empty;
    public string DetailJson { get; init; } = "{}";

    // Set by the log when appended.
    public long Seq { get; init; }
    public string PreviousHash { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
}

public sealed record AuditQuery
{
    public required string Scope { get; init; }
    public string? ActorId { get; init; }
    public string? ActionPrefix { get; init; }
    public string? Text { get; init; }
    public DateTimeOffset? Since { get; init; }
    public long? BeforeSeq { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed record AuditVerification(bool Valid, long Records, long? FirstBrokenSeq, string Message);

/// <summary>Append-only, tamper-evident record of what agents and people did.</summary>
public interface IAuditLog
{
    /// <summary>Appends unless an entry with the same key exists (idempotent).</summary>
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
    /// <summary>Recomputes the hash chain; any edited, inserted or deleted record breaks it.</summary>
    Task<AuditVerification> VerifyAsync(string scope, CancellationToken cancellationToken = default);
}

public static class AuditHasher
{
    public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>SHA-256 over the previous hash and every field of the record (not its own hash),
    /// each length-prefixed so no two different records serialize the same.</summary>
    /// <summary>Timestamps are hashed at microsecond precision, which is what Postgres stores.</summary>
    public static DateTimeOffset Normalize(DateTimeOffset at)
    {
        var utc = at.UtcDateTime;
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    public static string Compute(string previousHash, AuditEntry e)
    {
        var sb = new StringBuilder();
        void Add(string? v) => sb.Append((v ?? string.Empty).Length).Append(':').Append(v).Append('|');
        Add(previousHash);
        Add(e.Scope);
        Add(e.Seq.ToString());
        Add(e.Key);
        Add(e.At.UtcDateTime.ToString("O"));
        Add(e.ActorType);
        Add(e.ActorId);
        Add(e.ActorName);
        Add(e.Action);
        Add(e.Target);
        Add(e.SideEffects);
        Add(e.Outcome);
        Add(e.Summary);
        Add(e.DetailJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static AuditVerification Verify(IReadOnlyList<AuditEntry> ordered)
    {
        var previous = Genesis;
        long expectedSeq = 1;
        foreach (var e in ordered)
        {
            if (e.Seq != expectedSeq)
            {
                return new AuditVerification(false, ordered.Count, expectedSeq, $"Record {expectedSeq} is missing.");
            }

            if (e.PreviousHash != previous || Compute(previous, e) != e.Hash)
            {
                return new AuditVerification(false, ordered.Count, e.Seq, $"Record {e.Seq} doesn't match its hash: it was changed, or the chain was spliced.");
            }

            previous = e.Hash;
            expectedSeq++;
        }

        return new AuditVerification(true, ordered.Count, null, $"All {ordered.Count} records verified.");
    }
}

/// <summary>In-process audit log (tests and Memory mode). Same chaining as the durable one.</summary>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly Dictionary<string, List<AuditEntry>> _byScope = [];
    private readonly HashSet<string> _keys = [];
    private readonly Lock _lock = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_keys.Add(entry.Key)) return Task.CompletedTask;
            if (!_byScope.TryGetValue(entry.Scope, out var list)) _byScope[entry.Scope] = list = [];
            var previous = list.Count == 0 ? AuditHasher.Genesis : list[^1].Hash;
            var sequenced = entry with { At = AuditHasher.Normalize(entry.At), Seq = list.Count + 1, PreviousHash = previous };
            list.Add(sequenced with { Hash = AuditHasher.Compute(previous, sequenced) });
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery q, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IEnumerable<AuditEntry> list = _byScope.GetValueOrDefault(q.Scope) ?? [];
            if (q.ActorId is { } a) list = list.Where(e => e.ActorId == a);
            if (q.ActionPrefix is { } p) list = list.Where(e => e.Action.StartsWith(p, StringComparison.Ordinal));
            if (q.Text is { } t) list = list.Where(e => e.Summary.Contains(t, StringComparison.OrdinalIgnoreCase) || e.Target.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (q.Since is { } since) list = list.Where(e => e.At >= since);
            if (q.BeforeSeq is { } before) list = list.Where(e => e.Seq < before);
            return Task.FromResult<IReadOnlyList<AuditEntry>>(list.OrderByDescending(e => e.Seq).Take(q.Limit).ToList());
        }
    }

    public Task<AuditVerification> VerifyAsync(string scope, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(AuditHasher.Verify(_byScope.GetValueOrDefault(scope)?.ToList() ?? []));
        }
    }

    /// <summary>Test hook: simulate someone editing a stored record.</summary>
    public void TamperForTest(string scope, long seq, Func<AuditEntry, AuditEntry> edit)
    {
        lock (_lock)
        {
            var list = _byScope[scope];
            var i = list.FindIndex(e => e.Seq == seq);
            list[i] = edit(list[i]);
        }
    }
}
