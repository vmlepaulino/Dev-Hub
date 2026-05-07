using System.Net.Http;
using DevSprint.UI.Auth.Jira;

namespace DevSprint.UI.Auth.Confluence;

/// <summary>
/// Rewrites outgoing Confluence request URIs from
/// <c>https://api.atlassian.com/wiki/rest/...</c> to
/// <c>https://api.atlassian.com/ex/confluence/{cloudId}/wiki/rest/...</c>.
/// </summary>
/// <remarks>
/// <para>
/// Sibling of <see cref="JiraApiBaseUriHandler"/>. Confluence and Jira share
/// the same OAuth flow, the same access token, and the same cloudId — the
/// only difference at the gateway is the product segment in the path
/// (<c>/ex/jira/</c> vs <c>/ex/confluence/</c>).
/// </para>
/// <para>
/// Reuses <see cref="IJiraAuthService.GetCloudIdAsync"/> for the cloudid because
/// for a single Atlassian site the cloudid is identical across products. (The
/// site appears as two separate entries in <c>/oauth/token/accessible-resources</c>
/// — one for Jira, one for Confluence — but both carry the same <c>id</c>.)
/// </para>
/// </remarks>
public sealed class ConfluenceApiBaseUriHandler : DelegatingHandler
{
    private readonly IJiraAuthService _authService;

    public ConfluenceApiBaseUriHandler(IJiraAuthService authService)
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
        uri.AbsolutePath.StartsWith("/ex/confluence/", StringComparison.OrdinalIgnoreCase);

    private static Uri InsertCloudIdPrefix(Uri original, string cloudId)
    {
        var rewrittenPath = $"/ex/confluence/{cloudId}{original.AbsolutePath}";
        return new UriBuilder(original) { Path = rewrittenPath }.Uri;
    }
}
