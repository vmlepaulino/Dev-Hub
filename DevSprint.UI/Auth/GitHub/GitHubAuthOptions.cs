namespace DevSprint.UI.Auth.GitHub;

/// <summary>
/// Bound from configuration section <c>GitHub:OAuth</c>. The ClientId is NOT
/// a secret — it identifies the OAuth App registration. Anyone can read it.
/// </summary>
public sealed class GitHubAuthOptions
{
    public const string SectionName = "GitHub:OAuth";

    /// <summary>OAuth App Client ID (e.g. "Iv1.abc123def456..."). Required.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Space-separated OAuth scopes. Default covers reads needed by GitHubService
    /// (repo metadata, PRs, branches, user profiles).
    /// </summary>
    public string Scopes { get; set; } = "repo read:user read:org";

    /// <summary>
    /// Minimum poll interval in seconds. GitHub returns its own interval in
    /// the device-code response; this is just a floor.
    /// </summary>
    public int MinimumPollIntervalSeconds { get; set; } = 5;
}
