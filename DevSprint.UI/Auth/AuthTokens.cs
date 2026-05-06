namespace DevSprint.UI.Auth;

/// <summary>
/// Result of a successful OAuth exchange. Persisted (encrypted) and used by
/// <see cref="ITokenProvider"/> implementations.
/// </summary>
public sealed class AuthTokens
{
    /// <summary>The bearer access token used in API calls.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Token used to obtain a new access token without re-prompting the user.
    /// Empty when the platform issues non-expiring access tokens.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// UTC instant after which <see cref="AccessToken"/> should be considered
    /// expired. <see cref="DateTime.MaxValue"/> when the token doesn't expire.
    /// </summary>
    public DateTime AccessTokenExpiresAtUtc { get; set; } = DateTime.MaxValue;

    /// <summary>Space-separated list of OAuth scopes granted with the token.</summary>
    public string Scopes { get; set; } = string.Empty;

    /// <summary>True when the access token is past its expiry (with a 1-min safety margin).</summary>
    public bool IsAccessTokenExpired =>
        AccessTokenExpiresAtUtc != DateTime.MaxValue
        && DateTime.UtcNow >= AccessTokenExpiresAtUtc.AddMinutes(-1);

    /// <summary>True when refresh is possible (a refresh token has been issued).</summary>
    public bool CanRefresh => !string.IsNullOrEmpty(RefreshToken);
}
