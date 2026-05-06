using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace DevSprint.UI.Auth;

/// <summary>
/// HTTP message handler that stamps "Authorization: Bearer &lt;token&gt;" on every
/// outgoing request, asking an <see cref="ITokenProvider"/> for the current
/// access token. On a 401 it triggers a single retry to give the provider a
/// chance to refresh.
/// </summary>
/// <remarks>
/// Each platform gets its own subclass so the DI container can pick the right
/// <see cref="ITokenProvider"/> per typed HttpClient (GitHub vs Jira) without
/// resorting to keyed services.
/// </remarks>
public abstract class BearerTokenHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;

    protected BearerTokenHandler(ITokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await StampAuthorizationAsync(request, cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // 401 — token may have just expired or been revoked. Force-refresh once.
        response.Dispose();
        await StampAuthorizationAsync(request, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task StampAuthorizationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

/// <summary>Concrete handler for GitHub-typed HttpClients.</summary>
public sealed class GitHubBearerTokenHandler : BearerTokenHandler
{
    public GitHubBearerTokenHandler(GitHub.IGitHubAuthService authService) : base(authService) { }
}

/// <summary>Concrete handler for Jira-typed HttpClients.</summary>
public sealed class JiraBearerTokenHandler : BearerTokenHandler
{
    public JiraBearerTokenHandler(Jira.IJiraAuthService authService) : base(authService) { }
}
