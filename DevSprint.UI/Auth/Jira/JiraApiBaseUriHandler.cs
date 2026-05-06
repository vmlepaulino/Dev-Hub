using System.Net.Http;

namespace DevSprint.UI.Auth.Jira;

/// <summary>
/// Rewrites outgoing Jira request URIs from
/// <c>https://api.atlassian.com/rest/...</c> to
/// <c>https://api.atlassian.com/ex/jira/{cloudId}/rest/...</c>.
/// </summary>
/// <remarks>
/// <para>
/// Why this handler exists: OAuth 2.0 (3LO) tokens are scoped to a specific
/// Atlassian site by its <c>cloudId</c>, and the API gateway routes those tokens
/// through a path prefix. <see cref="Services.JiraService"/> still uses its
/// original relative URIs (e.g. <c>rest/api/3/search/jql</c>), so this handler
/// transparently inserts the prefix so the service code didn't need to change
/// when we moved off the API-token base URL.
/// </para>
/// <para>
/// Sits AFTER <see cref="JiraBearerTokenHandler"/> in the pipeline (so the
/// Authorization header is already attached) and BEFORE the network call.
/// </para>
/// </remarks>
public sealed class JiraApiBaseUriHandler : DelegatingHandler
{
    private readonly IJiraAuthService _authService;

    public JiraApiBaseUriHandler(IJiraAuthService authService)
    {
        _authService = authService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null)
        {
            var cloudId = await _authService.GetCloudIdAsync(cancellationToken);
            if (!string.IsNullOrEmpty(cloudId) && !IsAlreadyPrefixed(request.RequestUri))
            {
                request.RequestUri = InsertCloudIdPrefix(request.RequestUri, cloudId);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsAlreadyPrefixed(Uri uri) =>
        uri.AbsolutePath.StartsWith("/ex/jira/", StringComparison.OrdinalIgnoreCase);

    private static Uri InsertCloudIdPrefix(Uri original, string cloudId)
    {
        // Keep scheme, host, port, query, fragment; only rewrite the path.
        var rewrittenPath = $"/ex/jira/{cloudId}{original.AbsolutePath}";
        return new UriBuilder(original) { Path = rewrittenPath }.Uri;
    }
}
