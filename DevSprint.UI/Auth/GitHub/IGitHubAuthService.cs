namespace DevSprint.UI.Auth.GitHub;

/// <summary>
/// GitHub authentication surface used by the application. Implements
/// <see cref="ITokenProvider"/> so the BearerTokenHandler can ask for tokens
/// without knowing about device flow.
/// </summary>
public interface IGitHubAuthService : ITokenProvider
{
    /// <summary>True when a usable token (or refreshable one) is currently cached.</summary>
    bool IsSignedIn { get; }

    /// <summary>
    /// Ensures a usable token is available, performing silent refresh or, as a
    /// last resort, an interactive Device Flow sign-in. Called by
    /// <c>App.OnStartup</c> before the main window is shown.
    /// </summary>
    /// <returns>True on success, false if the user cancelled sign-in.</returns>
    Task<bool> EnsureSignedInAsync(CancellationToken cancellationToken = default);

    /// <summary>Wipes the GitHub entry from the token store. Next call will require sign-in.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
