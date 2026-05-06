using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DevSprint.UI.Onboarding;

/// <summary>
/// Persists the user's onboarding answers — including the Jira OAuth Client
/// Secret — to a single DPAPI-encrypted file at <c>%AppData%\TeamHub\config.dat</c>.
/// </summary>
/// <remarks>
/// <para>
/// Same threat model as <see cref="Auth.EncryptedTokenStore"/>: the file is
/// encrypted with <see cref="ProtectedData.Protect"/> under
/// <see cref="DataProtectionScope.CurrentUser"/>, so only the same Windows
/// user on the same machine can decrypt it. Copying the file to a different
/// account or machine renders it unreadable.
/// </para>
/// <para>
/// Stored shape is a flat <c>Dictionary&lt;string, string&gt;</c> using the
/// standard .NET configuration colon-separated keys (e.g.
/// <c>"GitHub:Repositories:0"</c>, <c>"Jira:OAuth:ClientId"</c>). The dictionary
/// is fed straight into <see cref="Microsoft.Extensions.Configuration.Memory.MemoryConfigurationProvider"/>
/// at startup so the rest of the app sees these values via the normal
/// <c>IConfiguration</c> pipeline.
/// </para>
/// </remarks>
public sealed class EncryptedConfigurationStore
{
    /// <summary>Distinct entropy from the token store so the two files can't be cross-decrypted accidentally.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DevSprint.TeamHub.Config.v1");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EncryptedConfigurationStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TeamHub");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "config.dat");
    }

    /// <summary>True when the encrypted config file exists on disk.</summary>
    public bool Exists => File.Exists(_filePath);

    /// <summary>
    /// Synchronously decrypts and returns the stored dictionary. Returns an
    /// empty dictionary when the file is absent or unreadable (not an error —
    /// caller treats "no file" as "first run").
    /// </summary>
    /// <remarks>
    /// Synchronous on purpose: <c>App.OnStartup</c> needs this value before
    /// the WPF dispatcher starts pumping, and ConfigurationBuilder doesn't
    /// support async sources.
    /// </remarks>
    public IDictionary<string, string?> Load()
    {
        if (!File.Exists(_filePath)) return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var encrypted = File.ReadAllBytes(_filePath);
            var decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions);
            return dict ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Corrupt / wrong account / wrong machine — start fresh. The wizard
            // will pre-fill from any other configuration source still available.
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Merges <paramref name="updates"/> into the stored dictionary and writes
    /// the result back, encrypted. Empty/null values are kept (so a wizard run
    /// can intentionally clear a field).
    /// </summary>
    public async Task SaveAsync(IDictionary<string, string?> updates, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = Load();
            foreach (var (key, value) in updates)
                existing[key] = value;

            var json = JsonSerializer.Serialize(existing, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(_filePath, encrypted, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Removes every <c>GitHub:Repositories:N</c> entry. Used before writing a new array.</summary>
    public async Task RemoveArrayAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = Load();
            var prefix = keyPrefix + ":";
            var toRemove = existing.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (toRemove.Count == 0) return;

            foreach (var k in toRemove) existing.Remove(k);

            var json = JsonSerializer.Serialize(existing, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(_filePath, encrypted, cancellationToken);
        }
        finally { _gate.Release(); }
    }
}
