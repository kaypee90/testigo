using AgentRuntime.Infrastructure.Identity;
using AgentRuntime.Tenancy;

namespace AgentRuntime.Tests;

public sealed class CredentialTests
{
    [Fact]
    public void Passwords_VerifyOnlyThemselves_AndHashesAreSalted()
    {
        var hash = PasswordHasher.Hash("correct horse battery", iterations: 1_000);
        Assert.True(PasswordHasher.Verify("correct horse battery", hash));
        Assert.False(PasswordHasher.Verify("correct horse batterY", hash));
        Assert.NotEqual(hash, PasswordHasher.Hash("correct horse battery", iterations: 1_000));
        Assert.DoesNotContain("correct horse", hash);
    }

    [Fact]
    public void OldHashes_AreFlaggedForRehash_AndGarbageNeverVerifies()
    {
        Assert.True(PasswordHasher.NeedsRehash(PasswordHasher.Hash("x", iterations: 1_000)));
        Assert.False(PasswordHasher.Verify("x", "plain-text"));
        Assert.False(PasswordHasher.Verify("x", "pbkdf2-sha256$1000$not-base64$zzz"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    public void WeakPasswords_AreRejected(string? password) => Assert.NotNull(PasswordHasher.Validate(password));
}

public sealed class TenancyUnitTests
{
    [Theory]
    [InlineData(null, "default")]
    [InlineData("", "default")]
    [InlineData("t-1", "t-1")]
    public void TenantIds_Normalize(string? input, string expected) => Assert.Equal(expected, TenantIds.Normalize(input));
}
