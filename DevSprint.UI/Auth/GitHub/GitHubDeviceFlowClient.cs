using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevSprint.UI.Auth.GitHub;

/// <summary>
/// Raw HTTP wrapper around the two GitHub OAuth endpoints used by Device Flow.
/// Knows nothing about token caching, dialogs, or the broader auth service.
/// </summary>
/// <remarks>
/// Uses a dedicated <see cref="HttpClient"/> registered under the name
/// <see cref="HttpClientName"/> so it can target <c>https://github.com/</c>
/// (NOT api.github.com) and so it never goes through <see cref="BearerTokenHandler"/>.
/// </remarks>
public sealed class GitHubDeviceFlowClient
{
    public const string HttpClientName = "github-auth";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;

    public GitHubDeviceFlowClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri("https://github.com/");
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DevSprint", "1.0"));
    }

    /// <summary>Step 1 — request a device code for the user to enter at github.com/login/device.</summary>
    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync(string clientId, string scopes, CancellationToken cancellationToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = scopes
        });

        using var response = await _httpClient.PostAsync("login/device/code", form, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(JsonOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException("GitHub returned an empty device-code response.");
    }

    /// <summary>Step 2 — exchange the device code for an access token. Call repeatedly until <see cref="DeviceTokenOutcome.Success"/>.</summary>
    public async Task<DeviceTokenResult> PollForTokenAsync(string clientId, string deviceCode, CancellationToken cancellationToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["device_code"] = deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
        });

        using var response = await _httpClient.PostAsync("login/oauth/access_token", form, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParsePollResponse(json);
    }

    /// <summary>Refresh an access token using a refresh token. GitHub rotates the refresh token on every call.</summary>
    public async Task<DeviceTokenResult> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellationToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        });

        using var response = await _httpClient.PostAsync("login/oauth/access_token", form, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParsePollResponse(json);
    }

    private static DeviceTokenResult ParsePollResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Success path: an access_token field is present.
        if (root.TryGetProperty("access_token", out var accessTokenEl) && accessTokenEl.ValueKind == JsonValueKind.String)
        {
            var accessToken = accessTokenEl.GetString() ?? string.Empty;
            var refreshToken = root.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String
                ? rt.GetString() ?? string.Empty
                : string.Empty;
            var expiresIn = root.TryGetProperty("expires_in", out var exp) && exp.ValueKind == JsonValueKind.Number
                ? exp.GetInt32()
                : 0;
            var scopes = root.TryGetProperty("scope", out var sc) && sc.ValueKind == JsonValueKind.String
                ? sc.GetString() ?? string.Empty
                : string.Empty;

            var expiresAtUtc = expiresIn > 0
                ? DateTime.UtcNow.AddSeconds(expiresIn)
                : DateTime.MaxValue;

            return new DeviceTokenResult
            {
                Outcome = DeviceTokenOutcome.Success,
                Tokens = new AuthTokens
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    AccessTokenExpiresAtUtc = expiresAtUtc,
                    Scopes = scopes
                }
            };
        }

        // Pending / slow-down / expired / denied paths come back as 200 with an "error" field.
        var error = root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
            ? errEl.GetString() ?? string.Empty
            : string.Empty;

        return new DeviceTokenResult
        {
            Outcome = error switch
            {
                "authorization_pending" => DeviceTokenOutcome.Pending,
                "slow_down" => DeviceTokenOutcome.SlowDown,
                "expired_token" => DeviceTokenOutcome.Expired,
                "access_denied" => DeviceTokenOutcome.Denied,
                "" => DeviceTokenOutcome.Failed,
                _ => DeviceTokenOutcome.Failed
            },
            ErrorCode = error
        };
    }

    public sealed class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")]    public string DeviceCode { get; set; } = string.Empty;
        [JsonPropertyName("user_code")]      public string UserCode { get; set; } = string.Empty;
        [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")]     public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")]       public int Interval { get; set; } = 5;
    }
}

public enum DeviceTokenOutcome
{
    Success,
    Pending,
    SlowDown,
    Expired,
    Denied,
    Failed
}

public sealed class DeviceTokenResult
{
    public DeviceTokenOutcome Outcome { get; set; }
    public AuthTokens? Tokens { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
}
