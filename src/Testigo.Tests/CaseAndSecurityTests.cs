using System.Security.Cryptography;
using System.Text;
using Testigo.Cases;
using Testigo.Security;

namespace Testigo.Tests;

/// <summary>FR-CASE-3: case statuses move only along the allowed transitions.</summary>
public class CaseStatusTests
{
    [Theory]
    [InlineData(CaseStatus.Received, CaseStatus.InTriage, true)]
    [InlineData(CaseStatus.InTriage, CaseStatus.Verified, true)]
    [InlineData(CaseStatus.Verified, CaseStatus.Referred, true)]
    [InlineData(CaseStatus.ClosedNoAction, CaseStatus.InTriage, true)]
    [InlineData(CaseStatus.Received, CaseStatus.Verified, false)]
    [InlineData(CaseStatus.NeedsInfo, CaseStatus.Verified, false)]
    [InlineData(CaseStatus.Withdrawn, CaseStatus.InTriage, false)]
    [InlineData(CaseStatus.Referred, CaseStatus.Received, false)]
    public void Only_listed_transitions_are_allowed(CaseStatus from, CaseStatus to, bool allowed) =>
        Assert.Equal(allowed, CaseStatusTransitions.CanMove(from, to));

    [Fact]
    public void Withdrawn_is_final_and_every_status_has_a_rule()
    {
        Assert.Empty(CaseStatusTransitions.From(CaseStatus.Withdrawn));
        foreach (var s in Enum.GetValues<CaseStatus>())
        {
            _ = CaseStatusTransitions.From(s);
            Assert.False(CaseStatusTransitions.CanMove(s, s));
        }
    }

    [Fact]
    public void Only_verification_needs_a_reviewer()
    {
        Assert.True(CaseStatusTransitions.NeedsReviewer(CaseStatus.Verified));
        Assert.All(Enum.GetValues<CaseStatus>().Where(s => s != CaseStatus.Verified), s => Assert.False(CaseStatusTransitions.NeedsReviewer(s)));
    }

    [Fact]
    public void Names_round_trip()
    {
        foreach (var s in Enum.GetValues<CaseStatus>())
        {
            Assert.True(CaseStatusTransitions.TryParse(CaseStatusTransitions.Name(s), out var back));
            Assert.Equal(s, back);
        }

        Assert.False(CaseStatusTransitions.TryParse("Verified", out _));
        Assert.False(CaseStatusTransitions.TryParse(null, out _));
    }
}

/// <summary>FR-INT-6: case codes are long, random and stored only as a keyed hash.</summary>
public class CaseCodeTests
{
    [Fact]
    public void Generated_codes_have_100_bits_and_normalize_back()
    {
        Assert.Equal(100, CaseCodes.BitsOfRandomness);
        var codes = Enumerable.Range(0, 200).Select(_ => CaseCodes.Generate()).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        foreach (var code in codes)
        {
            Assert.Matches("^[0-9A-Z]{4}(-[0-9A-Z]{4}){4}$", code);
            var normalized = CaseCodes.Normalize(code);
            Assert.NotNull(normalized);
            Assert.Equal(code, CaseCodes.Format(normalized));
        }
    }

    [Fact]
    public void Typing_mistakes_are_forgiven_but_garbage_is_refused()
    {
        Assert.Equal("01234567890123456719", CaseCodes.Normalize(" oi23-4567 89o1 2345-67l9 "));
        Assert.Equal("ABCDEFGH0123456789XY", CaseCodes.Normalize("abcd-efgh-0123-4567-89xy"));
        Assert.Null(CaseCodes.Normalize("ABCD-EFGH"));
        Assert.Null(CaseCodes.Normalize("ABCD-EFGH-0123-4567-89XU")); // U isn't in the alphabet
        Assert.Null(CaseCodes.Normalize(""));
        Assert.Null(CaseCodes.Normalize(null));
    }

    [Fact]
    public void Lookup_hash_depends_on_the_key()
    {
        var code = CaseCodes.Normalize(CaseCodes.Generate())!;
        var k1 = RandomNumberGenerator.GetBytes(32);
        var k2 = RandomNumberGenerator.GetBytes(32);
        var h1 = CaseCodes.LookupHash(code, k1);
        Assert.Equal(h1, CaseCodes.LookupHash(code, k1));
        Assert.NotEqual(h1, CaseCodes.LookupHash(code, k2));
        Assert.DoesNotContain(code, h1, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[0-9a-f]{64}$", h1);
    }
}

/// <summary>NFR-SEC-9: passphrases are kept only as Argon2id hashes.</summary>
public class PassphraseTests
{
    private static readonly PassphraseHasher Hasher = new(new PassphraseHashOptions { MemoryKiB = 1024, Iterations = 1, Parallelism = 1 });

    [Fact]
    public void Hash_verifies_only_the_same_passphrase()
    {
        var stored = Hasher.Hash("caballo bateria grapa correcta");
        Assert.StartsWith("argon2id$v=19$m=1024,t=1,p=1$", stored);
        Assert.DoesNotContain("caballo", stored);
        Assert.True(Hasher.Verify("caballo bateria grapa correcta", stored));
        Assert.True(Hasher.Verify("  caballo bateria grapa correcta ", stored));
        Assert.False(Hasher.Verify("caballo bateria grapa correct", stored));
        Assert.NotEqual(stored, Hasher.Hash("caballo bateria grapa correcta")); // salted
    }

    [Fact]
    public void Malformed_hashes_never_verify()
    {
        Assert.False(Hasher.Verify("lo que sea largo", "argon2id$v=19$m=1024"));
        Assert.False(Hasher.Verify("lo que sea largo", "bcrypt$abc"));
        Assert.False(Hasher.Verify("lo que sea largo", "argon2id$v=19$m=1024,t=1,p=1$***$***"));
    }

    [Fact]
    public void Short_passphrases_are_refused()
    {
        Assert.NotNull(Hasher.Problem("corta"));
        Assert.NotNull(Hasher.Problem("   "));
        Assert.Null(Hasher.Problem("una frase bastante larga"));
    }

    [Fact]
    public void Generated_passphrases_are_six_words_with_enough_entropy()
    {
        var phrase = PassphraseGenerator.Generate();
        Assert.Equal(PassphraseGenerator.Words, phrase.Split(' ').Length);
        Assert.True(PassphraseGenerator.Bits >= 50, $"only {PassphraseGenerator.Bits:F1} bits");
        Assert.Matches("^[a-z ]+$", phrase);
    }
}

/// <summary>NFR-SEC-4: field encryption with per-organization data keys wrapped by a master key.</summary>
public class EncryptionTests
{
    private static StaticMasterKeyProvider Masters(string active, params string[] ids) =>
        new(active, ids.ToDictionary(id => id, id => SHA256.HashData(Encoding.UTF8.GetBytes("test-master-" + id))));

    [Fact]
    public async Task Round_trip_and_ciphertext_hides_the_plaintext()
    {
        var cipher = new EnvelopeFieldCipher(new InMemoryDataKeyStore(), Masters("m1", "m1"), TimeProvider.System);
        var ct = await cipher.EncryptAsync("org_a", KeyPurpose.Content, "Texto ficticio del reporte", "report:1:free_text");
        Assert.DoesNotContain("ficticio", Encoding.UTF8.GetString(ct));
        Assert.Equal("Texto ficticio del reporte", await cipher.DecryptAsync("org_a", KeyPurpose.Content, ct, "report:1:free_text"));
    }

    [Fact]
    public async Task Wrong_context_or_tampering_fails()
    {
        var cipher = new EnvelopeFieldCipher(new InMemoryDataKeyStore(), Masters("m1", "m1"), TimeProvider.System);
        var ct = await cipher.EncryptAsync("org_a", KeyPurpose.Contact, "correo@ejemplo.invalid", "contact:1");
        await Assert.ThrowsAsync<DecryptionFailedException>(() => cipher.DecryptAsync("org_a", KeyPurpose.Contact, ct, "contact:2"));
        await Assert.ThrowsAsync<DecryptionFailedException>(() => cipher.DecryptAsync("org_b", KeyPurpose.Contact, ct, "contact:1"));
        await Assert.ThrowsAsync<DecryptionFailedException>(() => cipher.DecryptAsync("org_a", KeyPurpose.Content, ct, "contact:1"));
        var tampered = (byte[])ct.Clone();
        tampered[^1] ^= 0x01;
        await Assert.ThrowsAsync<DecryptionFailedException>(() => cipher.DecryptAsync("org_a", KeyPurpose.Contact, tampered, "contact:1"));
        await Assert.ThrowsAsync<DecryptionFailedException>(() => cipher.DecryptAsync("org_a", KeyPurpose.Contact, [1, 2, 3], "contact:1"));
    }

    [Fact]
    public async Task Contact_and_content_use_different_keys()
    {
        var store = new InMemoryDataKeyStore();
        var cipher = new EnvelopeFieldCipher(store, Masters("m1", "m1"), TimeProvider.System);
        await cipher.EncryptAsync("org_a", KeyPurpose.Content, "a", "x");
        await cipher.EncryptAsync("org_a", KeyPurpose.Contact, "b", "x");
        await cipher.EncryptAsync("org_b", KeyPurpose.Content, "c", "x");
        var keys = await store.ListAsync(default);
        Assert.Equal(3, keys.Select(k => k.KeyId).Distinct().Count());
        Assert.All(keys, k => Assert.Equal("m1", k.MasterKeyId));
    }

    [Fact]
    public async Task Rotation_keeps_old_data_readable()
    {
        var store = new InMemoryDataKeyStore();
        var cipher = new EnvelopeFieldCipher(store, Masters("m1", "m1"), TimeProvider.System);
        var before = await cipher.EncryptAsync("org_a", KeyPurpose.Content, "antes", "r");
        await cipher.RotateDataKeyAsync("org_a", KeyPurpose.Content);
        var after = await cipher.EncryptAsync("org_a", KeyPurpose.Content, "despues", "r");
        Assert.Equal("antes", await cipher.DecryptAsync("org_a", KeyPurpose.Content, before, "r"));
        Assert.Equal("despues", await cipher.DecryptAsync("org_a", KeyPurpose.Content, after, "r"));
        var keys = await store.ListAsync(default);
        Assert.Equal(2, keys.Count);
        Assert.Single(keys, k => k.RetiredAt is null);
    }

    [Fact]
    public async Task Rewrapping_moves_keys_to_the_new_master_key()
    {
        var store = new InMemoryDataKeyStore();
        var ct = await new EnvelopeFieldCipher(store, Masters("m1", "m1"), TimeProvider.System).EncryptAsync("org_a", KeyPurpose.Content, "dato", "r");
        var both = new EnvelopeFieldCipher(store, Masters("m2", "m1", "m2"), TimeProvider.System);
        Assert.Equal(1, await both.RewrapAllAsync());
        Assert.Equal(0, await both.RewrapAllAsync());
        var onlyNew = new EnvelopeFieldCipher(store, Masters("m2", "m2"), TimeProvider.System);
        Assert.Equal("dato", await onlyNew.DecryptAsync("org_a", KeyPurpose.Content, ct, "r"));
    }

    [Fact]
    public void Master_keys_must_be_256_bits() =>
        Assert.ThrowsAny<ArgumentException>(() => new StaticMasterKeyProvider("m1", new Dictionary<string, byte[]> { ["m1"] = new byte[16] }));
}
