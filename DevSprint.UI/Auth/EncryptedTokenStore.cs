using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DevSprint.UI.Auth;

/// <summary>
/// Persists per-platform <see cref="AuthTokens"/> to disk, encrypted with
/// Windows DPAPI under the current user's scope.
/// </summary>
/// <remarks>
/// The on-disk file is a Dictionary&lt;platformKey, AuthTokens&gt; serialised
/// to JSON, then passed through <see cref="ProtectedData.Protect"/>. Only the
/// same Windows user on the same machine can decrypt it; copying tokens.dat
/// to another machine or another account makes it unreadable.
///
/// All public members are safe for concurrent callers — operations are
/// serialised behind a <see cref="SemaphoreSlim"/>.
/// </remarks>
public sealed class EncryptedTokenStore
{
    public const string GitHubKey = "github";
    public const string JiraKey = "jira";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DevSprint.TeamHub.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, AuthTokens>? _cache;

    public EncryptedTokenStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TeamHub");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "tokens.dat");
    }

    /// <summary>Returns the saved tokens for <paramref name="platformKey"/>, or null if none.</summary>
    public async Task<AuthTokens?> GetAsync(string platformKey, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAsync(cancellationToken);
            return all.TryGetValue(platformKey, out var tokens) ? tokens : null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Saves <paramref name="tokens"/> under <paramref name="platformKey"/>, replacing any prior entry.</summary>
    public async Task SaveAsync(string platformKey, AuthTokens tokens, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAsync(cancellationToken);
            all[platformKey] = tokens;
            await PersistAsync(all, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Removes the entry for <paramref name="platformKey"/> if present.</summary>
    public async Task RemoveAsync(string platformKey, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAsync(cancellationToken);
            if (all.Remove(platformKey))
                await PersistAsync(all, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, AuthTokens>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return _cache;

        if (!File.Exists(_filePath))
        {
            _cache = new(StringComparer.OrdinalIgnoreCase);
            return _cache;
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_filePath, cancellationToken);
            var decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            _cache = JsonSerializer.Deserialize<Dictionary<string, AuthTokens>>(json, JsonOptions)
                     ?? new Dictionary<string, AuthTokens>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Corrupt or unreadable (e.g. user copied tokens.dat across accounts) — start fresh.
            _cache = new(StringComparer.OrdinalIgnoreCase);
        }

        return _cache;
    }

    private async Task PersistAsync(Dictionary<string, AuthTokens> all, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(all, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_filePath, encrypted, cancellationToken);
        _cache = all;
    }
}
