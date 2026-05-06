using System.IO;
using System.Text.Json;
using DevSprint.UI.Models;

namespace DevSprint.UI.Services;

public sealed class IdentityService : IIdentityService
{
    private readonly string _filePath;
    private readonly List<TeamIdentity> _identities = [];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IdentityService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TeamHub");
        Directory.CreateDirectory(appDataDir);
        _filePath = Path.Combine(appDataDir, "team-identities.json");
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_filePath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            var loaded = JsonSerializer.Deserialize<List<TeamIdentity>>(json, JsonOptions);
            if (loaded is not null)
            {
                _identities.Clear();
                _identities.AddRange(loaded);
            }
        }
        catch
        {
            // Corrupted file — start fresh
        }
    }

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_identities, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public IReadOnlyList<TeamIdentity> GetAll() => _identities.AsReadOnly();

    public TeamIdentity? GetByJiraAccountId(string accountId) =>
        _identities.FirstOrDefault(i => i.JiraAccountId.Equals(accountId, StringComparison.OrdinalIgnoreCase));

    public TeamIdentity? GetByGitHubUsername(string githubUsername) =>
        _identities.FirstOrDefault(i =>
            !string.IsNullOrWhiteSpace(i.GitHubUsername) &&
            i.GitHubUsername.Equals(githubUsername, StringComparison.OrdinalIgnoreCase));

    public TeamIdentity? GetCurrentUser() =>
        _identities.FirstOrDefault(i => i.IsCurrentUser);

    public void SetCurrentUser(TeamIdentity identity)
    {
        foreach (var i in _identities)
            i.IsCurrentUser = false;

        var existing = GetByJiraAccountId(identity.JiraAccountId);
        if (existing is not null)
        {
            existing.IsCurrentUser = true;
            existing.DisplayName = identity.DisplayName;
        }
        else
        {
            identity.IsCurrentUser = true;
            _identities.Add(identity);
        }
    }

    public void LinkGitHubUsername(TeamIdentity identity, string githubUsername)
    {
        if (string.IsNullOrWhiteSpace(identity.JiraAccountId) || string.IsNullOrWhiteSpace(githubUsername))
            return;

        var normalizedUsername = githubUsername.Trim();
        foreach (var existingIdentity in _identities.Where(i =>
            string.Equals(i.GitHubUsername, normalizedUsername, StringComparison.OrdinalIgnoreCase)))
        {
            existingIdentity.GitHubUsername = string.Empty;
        }

        var existing = GetByJiraAccountId(identity.JiraAccountId);
        if (existing is not null)
            existing.GitHubUsername = normalizedUsername;
    }

    public void MergeFromJira(IEnumerable<TeamIdentity> discovered)
    {
        foreach (var d in discovered)
        {
            if (string.IsNullOrEmpty(d.JiraAccountId)) continue;

            var existing = GetByJiraAccountId(d.JiraAccountId);
            if (existing is not null)
            {
                existing.JiraDisplayName = d.JiraDisplayName;
                existing.AvatarUrl = d.AvatarUrl;
                if (!string.IsNullOrEmpty(d.Email))
                    existing.Email = d.Email;
                if (string.IsNullOrEmpty(existing.DisplayName))
                    existing.DisplayName = d.JiraDisplayName;
            }
            else
            {
                if (string.IsNullOrEmpty(d.DisplayName))
                    d.DisplayName = d.JiraDisplayName;
                _identities.Add(d);
            }
        }
    }

    public string ResolveDisplayName(string jiraDisplayName)
    {
        if (string.IsNullOrEmpty(jiraDisplayName)) return jiraDisplayName;

        var match = _identities.FirstOrDefault(i =>
            i.JiraDisplayName.Equals(jiraDisplayName, StringComparison.OrdinalIgnoreCase));

        return match?.DisplayName ?? jiraDisplayName;
    }
}
