using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevSprint.UI.Auth.Jira;

/// <summary>
/// Raw HTTP wrapper around Atlassian's three OAuth 2.0 (3LO) endpoints:
/// <list type="bullet">
///   <item><c>https://auth.atlassian.com/authorize</c> — authorization URL builder (no HTTP call here).</item>
///   <item><c>https://auth.atlassian.com/oauth/token</c> — code exchange + refresh.</item>
///   <item><c>https://api.atlassian.com/oauth/token/accessible-resources</c> — cloudid lookup.</item>
/// </list>
/// Knows nothing about caching, dialogs, or the broader auth service.
/// </summary>
public sealed class JiraOAuthClient
{
    public const string HttpClientName = "jira-auth";

    private const string AuthorizeUrl = "https://auth.atlassian.com/authorize";
    private const string TokenUrl = "https://auth.atlassian.com/oauth/token";
    private const string AccessibleResourcesUrl = "https://api.atlassian.com/oauth/token/accessible-resources";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public JiraOAuthClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DevSprint", "1.0"));
    }

    /// <summary>
    /// Builds the authorize URL the user's browser is sent to. Adds <c>offline_access</c>
    /// to the requested scopes so Atlassian issues a refresh token. PKCE is enforced.
    /// </summary>
    public string BuildAuthorizeUrl(string clientId, string redirectUri, string scopes, string state, string codeChallenge)
    {
        // Atlassian appends offline_access automatically when present in the scope param.
        var scope = scopes.Contains("offline_access", StringComparison.Ordinal)
            ? scopes
            : (scopes + " offline_access").Trim();

        var query = new[]
        {
            ("audience", "api.atlassian.com"),
            ("client_id", clientId),
            ("scope", scope),
            ("redirect_uri", redirectUri),
            ("state", state),
            ("response_type", "code"),
            ("prompt", "consent"),
            ("code_challenge", codeChallenge),
            ("code_challenge_method", "S256")
        };

        var encoded = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Item1)}={Uri.EscapeDataString(kv.Item2)}"));
        return $"{AuthorizeUrl}?{encoded}";
    }

    /// <summary>Exchanges the authorization code (received on the loopback callback) for tokens.</summary>
    public async Task<AuthTokens> ExchangeCodeAsync(
        string clientId, string clientSecret, string code, string redirectUri, string codeVerifier,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            grant_type = "authorization_code",
            client_id = clientId,
            client_secret = clientSecret,
            code,
            redirect_uri = redirectUri,
            code_verifier = codeVerifier
        };

        return await PostTokenAsync(body, cancellationToken);
    }

    /// <summary>Refreshes the access token using a refresh token. Atlassian rotates refresh tokens.</summary>
    public async Task<AuthTokens> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken cancellationToken)
    {
        var body = new
        {
            grant_type = "refresh_token",
            client_id = clientId,
            client_secret = clientSecret,
            refresh_token = refreshToken
        };

        return await PostTokenAsync(body, cancellationToken);
    }

    /// <summary>
    /// Calls <c>/oauth/token/accessible-resources</c> with the access token and returns the
    /// matching cloudid for the configured base URL, or the first resource if no match.
    /// </summary>
    public async Task<AccessibleResource> GetAccessibleResourceAsync(string accessToken, string preferredSiteUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AccessibleResourcesUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var resources = await response.Content.ReadFromJsonAsync<List<AccessibleResource>>(JsonOptions, cancellationToken)
                        ?? new List<AccessibleResource>();

        if (resources.Count == 0)
            throw new InvalidOperationException(
                "Atlassian returned no accessible resources for this token. Check the user has access to the site and the requested scopes.");

        // Prefer the resource matching the configured site URL; otherwise take the first.
        var preferred = NormaliseUrl(preferredSiteUrl);
        var match = resources.FirstOrDefault(r => string.Equals(NormaliseUrl(r.Url), preferred, StringComparison.OrdinalIgnoreCase));
        return match ?? resources[0];
    }

    private async Task<AuthTokens> PostTokenAsync(object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = JsonContent.Create(body)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Atlassian token endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {json}");

        var payload = JsonSerializer.Deserialize<TokenResponse>(json, JsonOptions)
                      ?? throw new InvalidOperationException("Atlassian token endpoint returned an empty body.");

        if (string.IsNullOrEmpty(payload.AccessToken))
            throw new InvalidOperationException($"Atlassian token endpoint did not return an access_token. Body: {json}");

        return new AuthTokens
        {
            AccessToken = payload.AccessToken,
            RefreshToken = payload.RefreshToken ?? string.Empty,
            AccessTokenExpiresAtUtc = payload.ExpiresIn > 0
                ? DateTime.UtcNow.AddSeconds(payload.ExpiresIn)
                : DateTime.MaxValue,
            Scopes = payload.Scope ?? string.Empty
        };
    }

    private static string NormaliseUrl(string url) =>
        url.TrimEnd('/').ToLowerInvariant();

    public sealed class AccessibleResource
    {
        [JsonPropertyName("id")]     public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")]   public string Name { get; set; } = string.Empty;
        [JsonPropertyName("url")]    public string Url { get; set; } = string.Empty;
        [JsonPropertyName("scopes")] public List<string> Scopes { get; set; } = new();
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]  public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")]    public int ExpiresIn { get; set; }
        [JsonPropertyName("scope")]         public string? Scope { get; set; }
        [JsonPropertyName("token_type")]    public string? TokenType { get; set; }
    }
}
