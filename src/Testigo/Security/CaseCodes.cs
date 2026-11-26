using System.Security.Cryptography;
using System.Text;

namespace Testigo.Security;

/// <summary>
/// Case codes for anonymous follow-up (FR-INT-6, NFR-SEC-9): 20 characters of Crockford base32,
/// 100 bits of randomness, shown in groups of four (<c>K7QM-2XRA-9WTE-H4CN-V8PD</c>). Only an
/// HMAC of the normalized code is stored, so the database can find a case by code without holding
/// the code itself.
/// </summary>
public static class CaseCodes
{
    /// <summary>Crockford's alphabet: no I, L, O or U, so codes read back without confusion.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int Length = 20;
    public const int BitsOfRandomness = Length * 5;

    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return Format(new string(chars));
    }

    /// <summary>Groups of four, separated by dashes.</summary>
    public static string Format(string normalized)
    {
        var sb = new StringBuilder(normalized.Length + normalized.Length / 4);
        for (var i = 0; i < normalized.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append('-');
            sb.Append(normalized[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The code as typed back by a person: case, spaces and dashes don't matter, and the letters
    /// Crockford maps (O→0, I/L→1) are accepted. Null when it can't be a case code.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var sb = new StringBuilder(Length);
        foreach (var raw in input.Trim())
        {
            if (raw is '-' or ' ') continue;
            var c = char.ToUpperInvariant(raw) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            };
            if (!Alphabet.Contains(c)) return null;
            sb.Append(c);
        }

        return sb.Length == Length ? sb.ToString() : null;
    }

    /// <summary>The stored lookup value: HMAC-SHA256 of the normalized code under a server key.</summary>
    public static string LookupHash(string normalizedCode, ReadOnlySpan<byte> lookupKey)
    {
        var mac = HMACSHA256.HashData(lookupKey, Encoding.ASCII.GetBytes(normalizedCode));
        return Convert.ToHexStringLower(mac);
    }
}
