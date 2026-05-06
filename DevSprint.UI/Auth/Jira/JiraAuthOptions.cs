namespace DevSprint.UI.Auth.Jira;

/// <summary>
/// Bound from configuration section <c>Jira:OAuth</c>. Combines values from
/// <c>appsettings.json</c> (non-sensitive) and user-secrets (sensitive).
/// </summary>
public sealed class JiraAuthOptions
{
    public const string SectionName = "Jira:OAuth";

    /// <summary>OAuth 2.0 (3LO) Client ID from developer.atlassian.com. From user-secrets.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth 2.0 (3LO) Client Secret from developer.atlassian.com. From user-secrets.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Loopback port for the redirect URI. MUST match the Callback URL registered
    /// on the OAuth app. Atlassian validates the port exactly. From user-secrets.
    /// </summary>
    public int CallbackPort { get; set; }

    /// <summary>
    /// Space-separated OAuth scopes to request. <c>offline_access</c> is appended
    /// automatically so refresh tokens are issued.
    /// </summary>
    public string Scopes { get; set; } = "read:jira-work read:jira-user";
}
