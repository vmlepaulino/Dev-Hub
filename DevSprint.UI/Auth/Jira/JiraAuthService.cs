using Microsoft.Extensions.Options;

namespace DevSprint.UI.Auth.Jira;

/// <summary>
/// Orchestrates Jira authentication: loads cached tokens, silently refreshes
/// when expired, and falls back to the interactive OAuth 2.0 (3LO) browser
/// flow when neither is possible. Implements <see cref="ITokenProvider"/> so
/// HTTP calls can ask for a fresh token transparently.
/// </summary>
/// <remarks>
/// The interactive sign-in step is provided as a delegate
/// (<see cref="JiraInteractiveSignInDelegate"/>) injected via DI. This keeps the
/// service free of any direct WPF dependencies; <c>App.xaml.cs</c> wires up the
/// delegate that opens <c>JiraSignInDialog</c>.
/// </remarks>
public sealed class JiraAuthService : IJiraAuthService
{
    private readonly JiraOAuthClient _oauthClient;
    private readonly EncryptedTokenStore _store;
    private readonly JiraAuthOptions _options;
    private readonly JiraInteractiveSignInDelegate _interactiveSignIn;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AuthTokens? _cachedTokens;

    public JiraAuthService(
        JiraOAuthClient oauthClient,
        EncryptedTokenStore store,
        IOptions<JiraAuthOptions> options,
        JiraInteractiveSignInDelegate interactiveSignIn)
    {
        _oauthClient = oauthClient;
        _store = store;
        _options = options.Value;
        _interactiveSignIn = interactiveSignIn;

        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException(
                "Jira:OAuth:ClientId is not configured. Register an OAuth 2.0 (3LO) integration at developer.atlassian.com and set the value in user-secrets.");
        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException(
                "Jira:OAuth:ClientSecret is not configured. Set the value in user-secrets (dotnet user-secrets set \"Jira:OAuth:ClientSecret\" \"...\").");
        if (_options.CallbackPort <= 0)
            throw new InvalidOperationException(
                "Jira:OAuth:CallbackPort is not configured. Pick a port (e.g., 7890), register it as the Callback URL on the OAuth app, and store it in user-secrets.");
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
        var tokens = await EnsureValidTokensAsync(cancellationToken);
        return tokens.AccessToken;
    }

    public async Task<string> GetCloudIdAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await EnsureValidTokensAsync(cancellationToken);
        return tokens.CloudId;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _cachedTokens = null;
            await _store.RemoveAsync(EncryptedTokenStore.JiraKey, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Single funnel for token acquisition. Returns valid tokens or throws
    /// <see cref="OperationCanceledException"/> if the user cancels sign-in.
    /// </summary>
    private async Task<AuthTokens> EnsureValidTokensAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // 1. Hydrate cache from disk on first call.
            _cachedTokens ??= await _store.GetAsync(EncryptedTokenStore.JiraKey, cancellationToken);

            // 2. Cached and still valid (and we know the cloudid) → done.
            if (_cachedTokens is { IsAccessTokenExpired: false } && !string.IsNullOrEmpty(_cachedTokens.CloudId))
                return _cachedTokens;

            // 3. Cached but expired → try silent refresh. Cloudid is preserved.
            if (_cachedTokens is { CanRefresh: true })
            {
                var refreshed = await TryRefreshAsync(_cachedTokens, cancellationToken);
                if (refreshed is not null)
                {
                    _cachedTokens = refreshed;
                    await _store.SaveAsync(EncryptedTokenStore.JiraKey, refreshed, cancellationToken);
                    return refreshed;
                }
                // Refresh failed (refresh token revoked or expired) — fall through to interactive.
            }

            // 4. Interactive OAuth 2.0 (3LO) sign-in.
            var fresh = await _interactiveSignIn(_options, cancellationToken)
                        ?? throw new OperationCanceledException("Jira sign-in was cancelled.");

            _cachedTokens = fresh;
            await _store.SaveAsync(EncryptedTokenStore.JiraKey, fresh, cancellationToken);
            return fresh;
        }
        finally { _gate.Release(); }
    }

    private async Task<AuthTokens?> TryRefreshAsync(AuthTokens existing, CancellationToken cancellationToken)
    {
        try
        {
            var refreshed = await _oauthClient.RefreshAsync(_options.ClientId, _options.ClientSecret, existing.RefreshToken, cancellationToken);
            // Preserve cloudid — refresh doesn't return it.
            refreshed.CloudId = existing.CloudId;
            return refreshed;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Performs an interactive OAuth 2.0 (3LO) sign-in (open browser, await loopback,
/// exchange code, look up cloudid). Returns the resulting <see cref="AuthTokens"/>
/// (with <see cref="AuthTokens.CloudId"/> populated) on success, or null if cancelled.
/// </summary>
public delegate Task<AuthTokens?> JiraInteractiveSignInDelegate(JiraAuthOptions options, CancellationToken cancellationToken);
