using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Testigo.Security;

namespace Testigo.Infrastructure;

/// <summary>The master keys and the case-code lookup key, loaded once at startup.</summary>
public sealed class TestigoKeys
{
    public TestigoKeys(IMasterKeyProvider masterKeys, byte[] lookupKey)
    {
        if (lookupKey.Length < 32) throw new ArgumentException("The case-code lookup key must be at least 32 bytes.");
        MasterKeys = masterKeys;
        LookupKey = lookupKey;
    }

    public IMasterKeyProvider MasterKeys { get; }
    public byte[] LookupKey { get; }

    /// <summary>From configuration, or the key file (created first when allowed). Never logs key material.</summary>
    public static TestigoKeys Load(KeyOptions o, ILogger logger)
    {
        if (o.MasterKeys.Count > 0)
        {
            var active = o.ActiveMasterKey ?? (o.MasterKeys.Count == 1 ? o.MasterKeys.Keys.First() : throw new InvalidOperationException("Testigo:Keys:ActiveMasterKey is required with more than one master key."));
            if (string.IsNullOrWhiteSpace(o.LookupKey)) throw new InvalidOperationException("Testigo:Keys:LookupKey is required with configured master keys.");
            return new TestigoKeys(new StaticMasterKeyProvider(active, o.MasterKeys.ToDictionary(k => k.Key, k => Convert.FromBase64String(k.Value))), Convert.FromBase64String(o.LookupKey));
        }

        if (string.IsNullOrWhiteSpace(o.KeyFile))
        {
            throw new InvalidOperationException("Testigo needs encryption keys: set Testigo:Keys:KeyFile (a file outside the database) or Testigo:Keys:MasterKeys.");
        }

        if (!File.Exists(o.KeyFile))
        {
            if (!o.GenerateIfMissing) throw new InvalidOperationException($"The Testigo key file '{o.KeyFile}' doesn't exist.");
            var fresh = new KeyFile
            {
                ActiveMasterKey = "mk1",
                MasterKeys = new() { ["mk1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
                LookupKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            };
            WriteSecret(o.KeyFile, JsonSerializer.Serialize(fresh, KeyFileJson));
            logger.LogWarning("Created a new Testigo key file. Back it up apart from the database: without it the case data can't be read.");
        }

        var file = JsonSerializer.Deserialize<KeyFile>(File.ReadAllText(o.KeyFile), KeyFileJson)
            ?? throw new InvalidOperationException("The Testigo key file is empty.");
        if (file.MasterKeys.Count == 0 || string.IsNullOrWhiteSpace(file.ActiveMasterKey) || string.IsNullOrWhiteSpace(file.LookupKey))
        {
            throw new InvalidOperationException("The Testigo key file needs active_master_key, master_keys and lookup_key.");
        }

        return new TestigoKeys(new StaticMasterKeyProvider(file.ActiveMasterKey, file.MasterKeys.ToDictionary(k => k.Key, k => Convert.FromBase64String(k.Value))), Convert.FromBase64String(file.LookupKey));
    }

    /// <summary>The intake shared key, from configuration or its file (created first when allowed). Null closes intake.</summary>
    public static string? LoadIntakeKey(IntakeOptions o, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(o.SharedKey)) return o.SharedKey.Trim();
        if (string.IsNullOrWhiteSpace(o.SharedKeyFile)) return null;
        if (!File.Exists(o.SharedKeyFile))
        {
            if (!o.GenerateSharedKeyIfMissing) return null;
            WriteSecret(o.SharedKeyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), groupReadable: true);
            logger.LogInformation("Created the intake shared key file for the reporter app.");
        }

        var key = File.ReadAllText(o.SharedKeyFile).Trim();
        return key.Length >= 32 ? key : throw new InvalidOperationException("The intake shared key must be at least 32 characters.");
    }

    private static void WriteSecret(string path, string content, bool groupReadable = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | (groupReadable ? UnixFileMode.GroupRead : 0);
        }

        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static readonly JsonSerializerOptions KeyFileJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

    private sealed class KeyFile
    {
        public string ActiveMasterKey { get; set; } = string.Empty;
        public Dictionary<string, string> MasterKeys { get; set; } = [];
        public string LookupKey { get; set; } = string.Empty;
    }
}
