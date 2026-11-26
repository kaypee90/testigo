using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Testigo.Security;

/// <summary>
/// What a key protects. Contact details use a separate key from report content (NFR-SEC-4), so
/// access to one doesn't give the other.
/// </summary>
public enum KeyPurpose
{
    /// <summary>Report answers and free text, redaction maps, quotes, notes and messages.</summary>
    Content,
    /// <summary>A reporter's contact method and value.</summary>
    Contact
}

/// <summary>A data key as stored: wrapped (encrypted) by a master key, never in the clear.</summary>
public sealed record DataKeyRecord
{
    public required string KeyId { get; init; }
    public required string OrganizationId { get; init; }
    public required KeyPurpose Purpose { get; init; }
    /// <summary>nonce (12) | tag (16) | ciphertext (32), AES-256-GCM under the master key.</summary>
    public required byte[] WrappedKey { get; init; }
    public required string MasterKeyId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Set when a newer key replaced it: it still decrypts, but nothing new is encrypted with it.</summary>
    public DateTimeOffset? RetiredAt { get; init; }
}

/// <summary>Where wrapped data keys live (the database, in production).</summary>
public interface IDataKeyStore
{
    /// <summary>The key new data is encrypted with, or null when the organization has none for this purpose yet.</summary>
    Task<DataKeyRecord?> GetActiveAsync(string organizationId, KeyPurpose purpose, CancellationToken cancellationToken);
    Task<DataKeyRecord?> GetAsync(string keyId, CancellationToken cancellationToken);
    /// <summary>Adds a key and retires the previous active key for the same organization and purpose, atomically.</summary>
    Task AddActiveAsync(DataKeyRecord key, CancellationToken cancellationToken);
    Task<IReadOnlyList<DataKeyRecord>> ListAsync(CancellationToken cancellationToken);
    /// <summary>Replaces a key's wrapping (master key rotation). The data key itself doesn't change.</summary>
    Task UpdateWrappingAsync(string keyId, byte[] wrappedKey, string masterKeyId, CancellationToken cancellationToken);
}

/// <summary>
/// The master keys that wrap data keys. They come from configuration or a file outside the
/// database (NFR-SEC-4), so a database backup holds only ciphertext.
/// </summary>
public interface IMasterKeyProvider
{
    /// <summary>The master key new data keys are wrapped with.</summary>
    string ActiveKeyId { get; }
    /// <summary>A 32-byte key, or null when this server doesn't have it.</summary>
    byte[]? Get(string masterKeyId);
}

/// <summary>Master keys given directly (configuration, tests).</summary>
public sealed class StaticMasterKeyProvider : IMasterKeyProvider
{
    private readonly Dictionary<string, byte[]> keys;

    public StaticMasterKeyProvider(string activeKeyId, IReadOnlyDictionary<string, byte[]> keys)
    {
        foreach (var (id, key) in keys)
        {
            if (key.Length != 32) throw new ArgumentException($"Master key '{id}' must be 32 bytes (AES-256).");
        }

        if (!keys.ContainsKey(activeKeyId)) throw new ArgumentException($"The active master key '{activeKeyId}' isn't among the keys.");
        ActiveKeyId = activeKeyId;
        this.keys = new Dictionary<string, byte[]>(keys);
    }

    public string ActiveKeyId { get; }

    public byte[]? Get(string masterKeyId) => keys.GetValueOrDefault(masterKeyId);
}

/// <summary>
/// Field-level envelope encryption (NFR-SEC-4): AES-256-GCM with one data key per organization and
/// purpose, each wrapped by a master key. Every ciphertext is bound to what it belongs to (the
/// associated data, e.g. <c>report:{id}:free_text</c>), so a value copied into another row or
/// field fails to decrypt instead of being read in the wrong place.
/// </summary>
public interface IFieldCipher
{
    Task<byte[]> EncryptAsync(string organizationId, KeyPurpose purpose, string plaintext, string associatedData, CancellationToken cancellationToken = default);
    Task<string> DecryptAsync(string organizationId, KeyPurpose purpose, byte[] ciphertext, string associatedData, CancellationToken cancellationToken = default);
    /// <summary>A new data key for new data; existing data still decrypts with the old one.</summary>
    Task RotateDataKeyAsync(string organizationId, KeyPurpose purpose, CancellationToken cancellationToken = default);
    /// <summary>Re-wraps every data key with the active master key, so an old master key can be retired. Returns how many changed.</summary>
    Task<int> RewrapAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>A ciphertext couldn't be decrypted: wrong key, wrong place, or tampered with.</summary>
public sealed class DecryptionFailedException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class EnvelopeFieldCipher(IDataKeyStore store, IMasterKeyProvider masterKeys, TimeProvider clock) : IFieldCipher
{
    private const byte FormatVersion = 1;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyBytes = 32;

    private readonly ConcurrentDictionary<string, byte[]> unwrapped = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim creating = new(1, 1);

    public async Task<byte[]> EncryptAsync(string organizationId, KeyPurpose purpose, string plaintext, string associatedData, CancellationToken cancellationToken = default)
    {
        var record = await store.GetActiveAsync(organizationId, purpose, cancellationToken) ?? await CreateKeyAsync(organizationId, purpose, cancellationToken);
        var key = Unwrap(record);
        var keyId = Encoding.ASCII.GetBytes(record.KeyId);
        if (keyId.Length > byte.MaxValue) throw new InvalidOperationException("Key ids are at most 255 bytes.");

        var data = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[2 + keyId.Length + NonceBytes + TagBytes + data.Length];
        output[0] = FormatVersion;
        output[1] = (byte)keyId.Length;
        keyId.CopyTo(output, 2);
        var offset = 2 + keyId.Length;
        var nonce = output.AsSpan(offset, NonceBytes);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, data, output.AsSpan(offset + NonceBytes + TagBytes), output.AsSpan(offset + NonceBytes, TagBytes),
            Aad(organizationId, purpose, associatedData));
        return output;
    }

    public async Task<string> DecryptAsync(string organizationId, KeyPurpose purpose, byte[] ciphertext, string associatedData, CancellationToken cancellationToken = default)
    {
        if (ciphertext.Length < 2 || ciphertext[0] != FormatVersion) throw new DecryptionFailedException("Unknown ciphertext format.");
        var keyIdLength = ciphertext[1];
        var offset = 2 + keyIdLength;
        if (ciphertext.Length < offset + NonceBytes + TagBytes) throw new DecryptionFailedException("Ciphertext is truncated.");
        var keyId = Encoding.ASCII.GetString(ciphertext, 2, keyIdLength);
        var record = await store.GetAsync(keyId, cancellationToken) ?? throw new DecryptionFailedException("The data key isn't available.");
        if (record.OrganizationId != organizationId || record.Purpose != purpose) throw new DecryptionFailedException("The data key belongs elsewhere.");

        var key = Unwrap(record);
        var body = ciphertext.AsSpan(offset + NonceBytes + TagBytes);
        var plain = new byte[body.Length];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(ciphertext.AsSpan(offset, NonceBytes), body, ciphertext.AsSpan(offset + NonceBytes, TagBytes), plain,
                Aad(organizationId, purpose, associatedData));
        }
        catch (CryptographicException e)
        {
            throw new DecryptionFailedException("The ciphertext doesn't match its key or place.", e);
        }

        return Encoding.UTF8.GetString(plain);
    }

    public async Task RotateDataKeyAsync(string organizationId, KeyPurpose purpose, CancellationToken cancellationToken = default) =>
        await CreateKeyAsync(organizationId, purpose, cancellationToken, rotate: true);

    public async Task<int> RewrapAllAsync(CancellationToken cancellationToken = default)
    {
        var changed = 0;
        foreach (var record in await store.ListAsync(cancellationToken))
        {
            if (record.MasterKeyId == masterKeys.ActiveKeyId) continue;
            var key = Unwrap(record);
            await store.UpdateWrappingAsync(record.KeyId, Wrap(key, record.KeyId, record.OrganizationId, record.Purpose, masterKeys.ActiveKeyId), masterKeys.ActiveKeyId, cancellationToken);
            changed++;
        }

        return changed;
    }

    private async Task<DataKeyRecord> CreateKeyAsync(string organizationId, KeyPurpose purpose, CancellationToken cancellationToken, bool rotate = false)
    {
        await creating.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have created it while this one waited.
            if (!rotate && await store.GetActiveAsync(organizationId, purpose, cancellationToken) is { } existing) return existing;
            var keyId = $"dk_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";
            var key = RandomNumberGenerator.GetBytes(KeyBytes);
            var record = new DataKeyRecord
            {
                KeyId = keyId,
                OrganizationId = organizationId,
                Purpose = purpose,
                WrappedKey = Wrap(key, keyId, organizationId, purpose, masterKeys.ActiveKeyId),
                MasterKeyId = masterKeys.ActiveKeyId,
                CreatedAt = clock.GetUtcNow(),
            };
            await store.AddActiveAsync(record, cancellationToken);
            unwrapped[keyId] = key;
            return record;
        }
        finally
        {
            creating.Release();
        }
    }

    private byte[] Unwrap(DataKeyRecord record)
    {
        if (unwrapped.TryGetValue(record.KeyId, out var cached)) return cached;
        var master = masterKeys.Get(record.MasterKeyId) ?? throw new DecryptionFailedException($"Master key '{record.MasterKeyId}' isn't configured on this server.");
        var wrapped = record.WrappedKey;
        if (wrapped.Length != NonceBytes + TagBytes + KeyBytes) throw new DecryptionFailedException("A wrapped data key is malformed.");
        var key = new byte[KeyBytes];
        try
        {
            using var aes = new AesGcm(master, TagBytes);
            aes.Decrypt(wrapped.AsSpan(0, NonceBytes), wrapped.AsSpan(NonceBytes + TagBytes), wrapped.AsSpan(NonceBytes, TagBytes), key,
                WrapAad(record.KeyId, record.OrganizationId, record.Purpose));
        }
        catch (CryptographicException e)
        {
            throw new DecryptionFailedException("A data key doesn't unwrap with its master key.", e);
        }

        unwrapped[record.KeyId] = key;
        return key;
    }

    private byte[] Wrap(byte[] key, string keyId, string organizationId, KeyPurpose purpose, string masterKeyId)
    {
        var master = masterKeys.Get(masterKeyId) ?? throw new InvalidOperationException($"Master key '{masterKeyId}' isn't configured.");
        var output = new byte[NonceBytes + TagBytes + KeyBytes];
        RandomNumberGenerator.Fill(output.AsSpan(0, NonceBytes));
        using var aes = new AesGcm(master, TagBytes);
        aes.Encrypt(output.AsSpan(0, NonceBytes), key, output.AsSpan(NonceBytes + TagBytes), output.AsSpan(NonceBytes, TagBytes),
            WrapAad(keyId, organizationId, purpose));
        return output;
    }

    private static byte[] Aad(string organizationId, KeyPurpose purpose, string associatedData) =>
        Encoding.UTF8.GetBytes($"testigo.v1|{organizationId}|{purpose}|{associatedData}");

    private static byte[] WrapAad(string keyId, string organizationId, KeyPurpose purpose) =>
        Encoding.UTF8.GetBytes($"testigo.datakey.v1|{keyId}|{organizationId}|{purpose}");
}

/// <summary>An in-memory key store, for tests and the eval harness only.</summary>
public sealed class InMemoryDataKeyStore : IDataKeyStore
{
    private readonly ConcurrentDictionary<string, DataKeyRecord> keys = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public Task<DataKeyRecord?> GetActiveAsync(string organizationId, KeyPurpose purpose, CancellationToken cancellationToken) =>
        Task.FromResult(keys.Values.Where(k => k.OrganizationId == organizationId && k.Purpose == purpose && k.RetiredAt is null)
            .OrderByDescending(k => k.CreatedAt).FirstOrDefault());

    public Task<DataKeyRecord?> GetAsync(string keyId, CancellationToken cancellationToken) => Task.FromResult(keys.GetValueOrDefault(keyId));

    public Task AddActiveAsync(DataKeyRecord key, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            foreach (var old in keys.Values.Where(k => k.OrganizationId == key.OrganizationId && k.Purpose == key.Purpose && k.RetiredAt is null).ToList())
            {
                keys[old.KeyId] = old with { RetiredAt = key.CreatedAt };
            }

            keys[key.KeyId] = key;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DataKeyRecord>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DataKeyRecord>>(keys.Values.ToList());

    public Task UpdateWrappingAsync(string keyId, byte[] wrappedKey, string masterKeyId, CancellationToken cancellationToken)
    {
        keys[keyId] = keys[keyId] with { WrappedKey = wrappedKey, MasterKeyId = masterKeyId };
        return Task.CompletedTask;
    }
}
