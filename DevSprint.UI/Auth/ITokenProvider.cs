namespace DevSprint.UI.Auth;

/// <summary>
/// Minimal contract that <see cref="BearerTokenHandler"/> depends on. Each
/// platform's auth service implements this so the handler doesn't need to
/// know how the token was obtained.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Returns a usable access token. Implementations are expected to return a
    /// cached token when valid, refresh transparently when expired, and prompt
    /// the user only as a last resort.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the user cancels an interactive sign-in or the operation is
    /// cancelled via the token.
    /// </exception>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
