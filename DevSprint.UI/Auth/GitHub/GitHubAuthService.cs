using Microsoft.Extensions.Options;

namespace DevSprint.UI.Auth.GitHub;

/// <summary>
/// Orchestrates GitHub authentication: loads cached tokens, silently refreshes
/// when expired, and falls back to the interactive Device Flow dialog when
/// neither is possible. Implements <see cref="ITokenProvider"/> so HTTP calls
/// can ask for a fresh token transparently.
/// </summary>
/// <remarks>
/// The interactive sign-in step is provided as a delegate
/// (<see cref="InteractiveSignInDelegate"/>) injected via DI. This keeps the
/// service free of any direct WPF dependencies; <c>App.xaml.cs</c> wires up the
/// delegate that opens <c>DeviceCodeDialog</c>.
/// </remarks>
public sealed class GitHubAuthService : IGitHubAuthService
{
    private readonly GitHubDeviceFlowClient _deviceFlow;
    private readonly EncryptedTokenStore _store;
    private readonly GitHubAuthOptions _options;
    private readonly InteractiveSignInDelegate _interactiveSignIn;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AuthTokens? _cachedTokens;

    public GitHubAuthService(
        GitHubDeviceFlowClient deviceFlow,
        EncryptedTokenStore store,
        IOptions<GitHubAuthOptions> options,
        InteractiveSignInDelegate interactiveSignIn)
    {
        _deviceFlow = deviceFlow;
        _store = store;
        _options = options.Value;
        _interactiveSignIn = interactiveSignIn;

        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException(
                "GitHub:OAuth:ClientId is not configured. Register an OAuth App with Device Flow enabled and set the client id in appsettings.json or user-secrets.");
    }

    public bool IsSignedIn => _cachedTokens is not null
                              && (!_cachedTokens.IsAccessTokenExpired || _cachedTokens.CanRefresh);

    public async Task<bool> EnsureSignedInAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await GetAccessTokenAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // 1. Hydrate cache from disk on first call.
            _cachedTokens ??= await _store.GetAsync(EncryptedTokenStore.GitHubKey, cancellationToken);

            // 2. Cached and still valid → done.
            if (_cachedTokens is not null && !_cachedTokens.IsAccessTokenExpired)
                return _cachedTokens.AccessToken;

            // 3. Cached but expired → try silent refresh.
            if (_cachedTokens is { CanRefresh: true })
            {
                var refreshed = await TryRefreshAsync(_cachedTokens.RefreshToken, cancellationToken);
                if (refreshed is not null)
                {
                    _cachedTokens = refreshed;
                    await _store.SaveAsync(EncryptedTokenStore.GitHubKey, refreshed, cancellationToken);
                    return refreshed.AccessToken;
                }
                // Refresh failed (revoked / expired refresh token) — fall through to interactive.
            }

            // 4. Interactive Device Flow.
            var fresh = await _interactiveSignIn(_options, cancellationToken)
                        ?? throw new OperationCanceledException("GitHub sign-in was cancelled.");

            _cachedTokens = fresh;
            await _store.SaveAsync(EncryptedTokenStore.GitHubKey, fresh, cancellationToken);
            return fresh.AccessToken;
        }
        finally { _gate.Release(); }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _cachedTokens = null;
            await _store.RemoveAsync(EncryptedTokenStore.GitHubKey, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<AuthTokens?> TryRefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _deviceFlow.RefreshAsync(_options.ClientId, refreshToken, cancellationToken);
            return result.Outcome == DeviceTokenOutcome.Success ? result.Tokens : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Performs an interactive Device Flow sign-in (typically by opening a WPF
/// dialog). Implementations should return the resulting <see cref="AuthTokens"/>
/// on success, or null if the user cancelled.
/// </summary>
public delegate Task<AuthTokens?> InteractiveSignInDelegate(GitHubAuthOptions options, CancellationToken cancellationToken);
