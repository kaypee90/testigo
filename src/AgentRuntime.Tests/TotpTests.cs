using System.Text;
using AgentRuntime.Infrastructure.Identity;

namespace AgentRuntime.Tests;

/// <summary>The authenticator-app codes behind two-factor sign-in (Identity/Totp.cs).</summary>
public sealed class TotpTests
{
    /// <summary>RFC 6238's SHA-1 key: the ASCII bytes of "12345678901234567890".</summary>
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    // RFC 6238 appendix B (SHA-1), last six of its eight digits.
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Codes_match_the_rfc_test_vectors(long unixSeconds, string expected) =>
        Assert.Equal(expected, Totp.Code(RfcSecret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))));

    [Fact]
    public void A_code_is_accepted_one_step_either_side_for_clock_drift_but_not_further()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111109);
        var step = Totp.StepAt(now);

        Assert.Equal(step - 1, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 1), now, lastUsedStep: 0));
        Assert.Equal(step + 1, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 1), now, lastUsedStep: 0));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 2), now, lastUsedStep: 0));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 2), now, lastUsedStep: 0));
    }

    [Fact]
    public void A_code_already_used_is_refused()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var step = Totp.StepAt(now);
        var code = Totp.Code(RfcSecret, step);

        Assert.Equal(step, Totp.Verify(RfcSecret, code, now, lastUsedStep: step - 1));
        Assert.Null(Totp.Verify(RfcSecret, code, now, lastUsedStep: step));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Malformed_codes_are_refused(string code) =>
        Assert.Null(Totp.Verify(RfcSecret, code, DateTimeOffset.UtcNow, lastUsedStep: 0));

    [Fact]
    public void Secrets_round_trip_through_base32()
    {
        // RFC 4648's example, without padding.
        Assert.Equal("MZXW6YTBOI", Totp.ToBase32(Encoding.ASCII.GetBytes("foobar")));
        Assert.Equal("foobar", Encoding.ASCII.GetString(Totp.FromBase32("mzxw 6ytb oi")));

        var secret = Totp.NewSecret();
        Assert.Equal(20, secret.Length);
        Assert.Equal(secret, Totp.FromBase32(Totp.ToBase32(secret)));
    }

    [Fact]
    public void The_app_link_names_the_issuer_and_account()
    {
        var uri = Totp.Uri("Testigo", "jane@acme.test", Encoding.ASCII.GetBytes("foobar"));
        Assert.Equal("otpauth://totp/Testigo:jane%40acme.test?secret=MZXW6YTBOI&issuer=Testigo&algorithm=SHA1&digits=6&period=30", uri);
    }
}
