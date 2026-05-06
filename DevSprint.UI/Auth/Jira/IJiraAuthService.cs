namespace DevSprint.UI.Auth.Jira;

/// <summary>
/// Jira authentication surface used by the application. Implements
/// <see cref="ITokenProvider"/> so JiraBearerTokenHandler can ask for tokens
/// without knowing about OAuth 2.0 (3LO) or PKCE.
/// </summary>
public interface IJiraAuthService : ITokenProvider
{
    /// <summary>True when a usable token (or a refreshable one) is currently cached.</summary>
    bool IsSignedIn { get; }

    /// <summary>
    /// Returns the Atlassian Cloud ID associated with the signed-in account.
    /// Used by JiraApiBaseUriHandler to rewrite request URIs from
    /// <c>/rest/...</c> to <c>/ex/jira/{cloudId}/rest/...</c>.
    /// Triggers sign-in if necessary.
    /// </summary>
    Task<string> GetCloudIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a usable token is available, performing silent refresh or, as a
    /// last resort, an interactive OAuth 2.0 (3LO) sign-in. Called by
    /// <c>App.OnStartup</c> before the main window is shown.
    /// </summary>
    /// <returns>True on success, false if the user cancelled sign-in.</returns>
    Task<bool> EnsureSignedInAsync(CancellationToken cancellationToken = default);

    /// <summary>Wipes the Jira entry from the token store. Next call requires sign-in.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
