using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Testigo.Security;

/// <summary>Argon2id cost settings (FR-INT-6, NFR-SEC-9). Configuration section <c>Testigo:Passphrase</c>.</summary>
public sealed record PassphraseHashOptions
{
    public const string Section = "Testigo:Passphrase";

    /// <summary>Memory in KiB (default 64 MiB, OWASP's minimum recommendation for Argon2id is 19 MiB).</summary>
    public int MemoryKiB { get; init; } = 65536;
    public int Iterations { get; init; } = 3;
    public int Parallelism { get; init; } = 1;
    /// <summary>The shortest passphrase a reporter may choose.</summary>
    public int MinimumLength { get; init; } = 12;
}

/// <summary>
/// Hashes and checks follow-up passphrases with Argon2id. The stored form is self-describing
/// (<c>argon2id$v=19$m=…,t=…,p=…$salt$hash</c>), so the cost can be raised later without breaking
/// existing hashes. The passphrase itself is never stored.
/// </summary>
public sealed class PassphraseHasher(PassphraseHashOptions options)
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public string Hash(string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(passphrase, salt, options.MemoryKiB, options.Iterations, options.Parallelism);
        return $"argon2id$v=19$m={options.MemoryKiB},t={options.Iterations},p={options.Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Constant-time comparison. A malformed stored value never matches.</summary>
    public bool Verify(string passphrase, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 5 || parts[0] != "argon2id" || parts[1] != "v=19") return false;
        int m = 0, t = 0, p = 0;
        foreach (var kv in parts[2].Split(','))
        {
            var pair = kv.Split('=');
            if (pair.Length != 2 || !int.TryParse(pair[1], out var v) || v <= 0) return false;
            switch (pair[0])
            {
                case "m": m = v; break;
                case "t": t = v; break;
                case "p": p = v; break;
                default: return false;
            }
        }

        if (m == 0 || t == 0 || p == 0) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Compute(passphrase, salt, m, t, p);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Why a chosen passphrase isn't accepted, or null when it is.</summary>
    public string? Problem(string? passphrase) =>
        string.IsNullOrWhiteSpace(passphrase) || passphrase.Trim().Length < options.MinimumLength
            ? $"La frase debe tener al menos {options.MinimumLength} caracteres."
            : null;

    private static byte[] Compute(string passphrase, byte[] salt, int memoryKiB, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(passphrase.Trim().Normalize(NormalizationForm.FormC)))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(HashBytes);
    }
}

/// <summary>
/// Generated passphrases for reporters who don't choose one (FR-INT-6): six words from a list of
/// common Spanish words without accents, so they are easy to write down and type on any phone.
/// </summary>
public static class PassphraseGenerator
{
    public const int Words = 6;

    private static readonly Lazy<string[]> WordList = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Testigo.Data.palabras-es.txt")
            ?? throw new InvalidOperationException("The passphrase word list isn't embedded.");
        using var reader = new StreamReader(stream);
        var words = reader.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => !w.StartsWith('#'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (words.Length < 256) throw new InvalidOperationException("The passphrase word list needs at least 256 words.");
        return words;
    });

    public static int ListSize => WordList.Value.Length;

    /// <summary>Bits of randomness in a generated passphrase.</summary>
    public static double Bits => Words * Math.Log2(ListSize);

    public static string Generate()
    {
        var list = WordList.Value;
        return string.Join(' ', Enumerable.Range(0, Words).Select(_ => list[RandomNumberGenerator.GetInt32(list.Length)]));
    }
}
