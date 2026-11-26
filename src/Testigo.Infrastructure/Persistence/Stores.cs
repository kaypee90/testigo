using Microsoft.EntityFrameworkCore;
using Testigo.Security;
using Testigo.Triage;

namespace Testigo.Infrastructure.Persistence;

/// <summary>Data keys in Postgres. Only wrapped keys are stored (NFR-SEC-4).</summary>
public sealed class EfDataKeyStore(IDbContextFactory<TestigoDbContext> factory) : IDataKeyStore
{
    public async Task<DataKeyRecord?> GetActiveAsync(string organizationId, KeyPurpose purpose, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var p = purpose.ToString();
        var row = await db.DataKeys.AsNoTracking()
            .Where(k => k.OrganizationId == organizationId && k.Purpose == p && k.RetiredAt == null)
            .OrderByDescending(k => k.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<DataKeyRecord?> GetAsync(string keyId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.DataKeys.AsNoTracking().FirstOrDefaultAsync(k => k.KeyId == keyId, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task AddActiveAsync(DataKeyRecord key, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var p = key.Purpose.ToString();
        await db.DataKeys.Where(k => k.OrganizationId == key.OrganizationId && k.Purpose == p && k.RetiredAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RetiredAt, key.CreatedAt), cancellationToken);
        db.DataKeys.Add(new DataKeyRow
        {
            KeyId = key.KeyId,
            OrganizationId = key.OrganizationId,
            Purpose = p,
            WrappedKey = key.WrappedKey,
            MasterKeyId = key.MasterKeyId,
            CreatedAt = key.CreatedAt,
        });
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DataKeyRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return [.. (await db.DataKeys.AsNoTracking().ToListAsync(cancellationToken)).Select(ToRecord)];
    }

    public async Task UpdateWrappingAsync(string keyId, byte[] wrappedKey, string masterKeyId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.DataKeys.Where(k => k.KeyId == keyId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.WrappedKey, wrappedKey).SetProperty(k => k.MasterKeyId, masterKeyId), cancellationToken);
    }

    private static DataKeyRecord ToRecord(DataKeyRow r) => new()
    {
        KeyId = r.KeyId,
        OrganizationId = r.OrganizationId,
        Purpose = Enum.Parse<KeyPurpose>(r.Purpose),
        WrappedKey = r.WrappedKey,
        MasterKeyId = r.MasterKeyId,
        CreatedAt = r.CreatedAt,
        RetiredAt = r.RetiredAt,
    };
}

/// <summary>The triage cache in Postgres, per organization and encrypted under its content key.</summary>
public sealed class EfTriageCache(IDbContextFactory<TestigoDbContext> factory, IFieldCipher cipher) : ITriageCache
{
    public async Task<string?> GetAsync(string organizationId, string cacheKey, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.TriageCache.AsNoTracking().FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.CacheKey == cacheKey, cancellationToken);
        if (row is null) return null;
        try
        {
            return await cipher.DecryptAsync(organizationId, KeyPurpose.Content, row.ArgumentsCipher, Aad(cacheKey), cancellationToken);
        }
        catch (DecryptionFailedException)
        {
            return null; // an unreadable entry is a miss, never an error for the reporter
        }
    }

    public async Task PutAsync(string organizationId, string cacheKey, string argumentsJson, CancellationToken cancellationToken)
    {
        var ct = await cipher.EncryptAsync(organizationId, KeyPurpose.Content, argumentsJson, Aad(cacheKey), cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (await db.TriageCache.AnyAsync(c => c.OrganizationId == organizationId && c.CacheKey == cacheKey, cancellationToken)) return;
        db.TriageCache.Add(new TriageCacheRow { OrganizationId = organizationId, CacheKey = cacheKey, ArgumentsCipher = ct, CreatedAt = DateTimeOffset.UtcNow });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another worker cached the same answer first.
        }
    }

    private static string Aad(string cacheKey) => $"triage_cache:{cacheKey}";
}
