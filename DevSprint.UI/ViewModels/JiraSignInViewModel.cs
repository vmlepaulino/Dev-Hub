using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevSprint.UI.Auth;
using DevSprint.UI.Auth.Jira;
using Microsoft.Extensions.Configuration;

namespace DevSprint.UI.ViewModels;

/// <summary>
/// Drives <see cref="Views.JiraSignInDialog"/>. Runs the OAuth 2.0 (3LO) flow:
/// starts the loopback listener, opens the browser, waits for the redirect,
/// exchanges the code for tokens, looks up the cloudid, and raises
/// <see cref="Completed"/> with the resulting <see cref="AuthTokens"/> (or null
/// if the user cancelled / something failed).
/// </summary>
public sealed partial class JiraSignInViewModel : ObservableObject
{
    private readonly JiraOAuthClient _oauthClient;
    private readonly JiraAuthOptions _options;
    private readonly string _preferredSiteUrl;
    private readonly CancellationTokenSource _cts = new();

    private string _authorizeUrl = string.Empty;

    [ObservableProperty] private string _statusMessage = "Starting Jira sign-in…";
    [ObservableProperty] private bool _isWorking = true;
    [ObservableProperty] private bool _hasError;

    /// <summary>Raised exactly once when the flow ends.</summary>
    public event EventHandler<AuthTokens?>? Completed;

    public JiraSignInViewModel(JiraOAuthClient oauthClient, JiraAuthOptions options, IConfiguration configuration)
    {
        _oauthClient = oauthClient;
        _options = options;
        _preferredSiteUrl = configuration["Jira:BaseUrl"] ?? string.Empty;
    }

    /// <summary>Kicks off the flow. Call once when the dialog loads.</summary>
    public async Task StartAsync()
    {
        try
        {
            // 1. Generate one-shot PKCE + state.
            var (verifier, challenge) = PkceCodes.Generate();
            var state = PkceCodes.GenerateState();

            // 2. Spin up the loopback listener.
            using var listener = new LoopbackHttpListener(_options.CallbackPort);

            // 3. Build authorize URL and open the browser.
            _authorizeUrl = _oauthClient.BuildAuthorizeUrl(
                _options.ClientId, listener.CallbackUrl, _options.Scopes, state, challenge);

            StatusMessage = "Waiting for sign-in in your browser…";
            OpenBrowser(_authorizeUrl);

            // 4. Await the redirect.
            var query = await listener.AwaitCallbackAsync(_cts.Token);

            // 5. Validate state, surface any provider error.
            if (query.TryGetValue("error", out var error) && !string.IsNullOrEmpty(error))
                throw new InvalidOperationException(
                    $"Atlassian returned error '{error}' ({query.GetValueOrDefault("error_description") ?? "no description"}).");

            if (!query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
                throw new InvalidOperationException("Atlassian redirect did not include an authorization code.");

            if (!query.TryGetValue("state", out var returnedState) || returnedState != state)
                throw new InvalidOperationException(
                    "OAuth state mismatch — the redirect didn't match this sign-in attempt. Aborting for safety.");

            // 6. Exchange code for tokens.
            StatusMessage = "Exchanging code for tokens…";
            var tokens = await _oauthClient.ExchangeCodeAsync(
                _options.ClientId, _options.ClientSecret, code, listener.CallbackUrl, verifier, _cts.Token);

            // 7. Look up cloudid for the configured site.
            StatusMessage = "Resolving Atlassian site…";
            var resource = await _oauthClient.GetAccessibleResourceAsync(tokens.AccessToken, _preferredSiteUrl, _cts.Token);
            tokens.CloudId = resource.Id;

            StatusMessage = $"Signed in to {resource.Name}.";
            IsWorking = false;
            Completed?.Invoke(this, tokens);
        }
        catch (OperationCanceledException)
        {
            // user cancelled — Completed already raised by CancelCommand
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Jira sign-in failed: {ex.Message}";
            IsWorking = false;
            Completed?.Invoke(this, null);
        }
    }

    [RelayCommand]
    private void OpenBrowserAgain()
    {
        if (!string.IsNullOrEmpty(_authorizeUrl))
            OpenBrowser(_authorizeUrl);
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts.Cancel();
        StatusMessage = "Sign-in cancelled.";
        IsWorking = false;
        Completed?.Invoke(this, null);
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* best-effort */ }
    }
}
