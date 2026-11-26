using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AgentRuntime.Infrastructure.Identity;

/// <summary>
/// Time-based one-time passwords (RFC 6238), the codes authenticator apps show: HMAC-SHA1, six
/// digits, a new code every 30 seconds. What Google Authenticator, Microsoft Authenticator, 1Password
/// and the like expect by default.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int PeriodSeconds = 30;
    private const int SecretBytes = 20; // 160 bits, as RFC 4226 recommends

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    public static long StepAt(DateTimeOffset at) => at.ToUnixTimeSeconds() / PeriodSeconds;

    public static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>The step <paramref name="code"/> belongs to, if it's right for now (one step either
    /// side, for clock drift) and newer than <paramref name="lastUsedStep"/>, so a code can't be
    /// replayed; null otherwise.</summary>
    public static long? Verify(byte[] secret, string code, DateTimeOffset now, long lastUsedStep, int window = 1)
    {
        if (code.Length != Digits || !code.All(char.IsAsciiDigit)) return null;
        var current = StepAt(now);
        var presented = Encoding.ASCII.GetBytes(code);
        long? match = null;
        // Every candidate is checked, in constant time, so timing doesn't show which one matched.
        for (var step = current - window; step <= current + window; step++)
        {
            var same = CryptographicOperations.FixedTimeEquals(presented, Encoding.ASCII.GetBytes(Code(secret, step)));
            if (same && step > lastUsedStep) match = step;
        }

        return match;
    }

    /// <summary>The <c>otpauth://</c> link authenticator apps read from the QR code.</summary>
    public static string Uri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}" +
        $"?secret={ToBase32(secret)}&issuer={System.Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>RFC 4648 base32 without padding: how secrets are typed into an app by hand.</summary>
    public static string ToBase32(byte[] data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0) output.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    public static byte[] FromBase32(string text)
    {
        var clean = text.Trim().TrimEnd('=').Replace(" ", string.Empty).ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException("Not a base32 secret.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
