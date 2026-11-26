using AgentRuntime.Safety;

namespace AgentRuntime.Tests;

public sealed class AuditLogTests
{
    private static AuditEntry Entry(string key, string summary = "did something") => new()
    {
        Scope = "ws-1", Key = key, ActorType = "agent", ActorId = "agent-1", Action = "tool.call", Target = "crm__get", Summary = summary,
        At = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567) // sub-microsecond ticks are dropped
    };

    [Fact]
    public async Task Appends_AreChained_Sequenced_AndIdempotentByKey()
    {
        var log = new InMemoryAuditLog();
        await log.AppendAsync(Entry("k1"));
        await log.AppendAsync(Entry("k2"));
        await log.AppendAsync(Entry("k1")); // a replay after a crash

        var entries = (await log.QueryAsync(new AuditQuery { Scope = "ws-1" })).OrderBy(e => e.Seq).ToList();
        Assert.Equal([1L, 2L], entries.Select(e => e.Seq));
        Assert.Equal(AuditHasher.Genesis, entries[0].PreviousHash);
        Assert.Equal(entries[0].Hash, entries[1].PreviousHash);
        Assert.Equal(0, entries[0].At.Ticks % 10);
        Assert.True((await log.VerifyAsync("ws-1")).Valid);
    }

    [Fact]
    public async Task EditingARecord_BreaksTheChain_AtThatRecord()
    {
        var log = new InMemoryAuditLog();
        for (var i = 1; i <= 3; i++) await log.AppendAsync(Entry($"k{i}"));

        log.TamperForTest("ws-1", 2, e => e with { Summary = "nothing to see here" });
        var result = await log.VerifyAsync("ws-1");

        Assert.False(result.Valid);
        Assert.Equal(2, result.FirstBrokenSeq);
    }

    [Fact]
    public void RecomputingTheHash_AfterAnEdit_StillBreaksTheNextLink()
    {
        var a = Entry("k1") with { Seq = 1, PreviousHash = AuditHasher.Genesis };
        a = a with { Hash = AuditHasher.Compute(AuditHasher.Genesis, a) };
        var b = Entry("k2") with { Seq = 2, PreviousHash = a.Hash };
        b = b with { Hash = AuditHasher.Compute(a.Hash, b) };

        var forged = a with { Summary = "forged" };
        forged = forged with { Hash = AuditHasher.Compute(AuditHasher.Genesis, forged) };

        Assert.True(AuditHasher.Verify([a, b]).Valid);
        Assert.Equal(2, AuditHasher.Verify([forged, b]).FirstBrokenSeq);
        Assert.Equal(1, AuditHasher.Verify([b]).FirstBrokenSeq); // a deleted first record
    }

    [Fact]
    public void FieldBoundaries_AreUnambiguous()
    {
        var x = Entry("k") with { ActorName = "ab", Action = "c" };
        var y = Entry("k") with { ActorName = "a", Action = "bc" };
        Assert.NotEqual(AuditHasher.Compute(AuditHasher.Genesis, x), AuditHasher.Compute(AuditHasher.Genesis, y));
    }
}
