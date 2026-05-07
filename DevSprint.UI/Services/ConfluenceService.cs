using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DevSprint.UI.Models;
using Microsoft.Extensions.Configuration;

namespace DevSprint.UI.Services;

/// <summary>
/// Read-only Confluence Cloud client. Phase 1 surface area: CQL search for
/// pages mentioning a Jira issue key, with creator + last-editor identities.
/// </summary>
/// <remarks>
/// HttpClient is wired by <see cref="Microsoft.Extensions.Http.IHttpClientFactory"/> with two
/// <see cref="DelegatingHandler"/>s installed:
/// <list type="bullet">
///   <item><see cref="Auth.JiraBearerTokenHandler"/> — same Atlassian access token as Jira.</item>
///   <item><see cref="Auth.Confluence.ConfluenceApiBaseUriHandler"/> — rewrites paths to /ex/confluence/{cloudId}/.</item>
/// </list>
/// The constructor must NOT touch authorization or BaseAddress prefixes.
/// </remarks>
public sealed class ConfluenceService : IConfluenceService
{
    private readonly HttpClient _httpClient;
    private readonly string _siteBaseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ConfluenceService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        // Bare gateway base — handlers prepend /ex/confluence/{cloudId}.
        _httpClient.BaseAddress = new Uri("https://api.atlassian.com/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Used to convert Confluence's relative URLs (page links, avatars) into
        // browser-openable absolute URLs. Same site as Jira.
        _siteBaseUrl = (configuration["Jira:BaseUrl"] ?? string.Empty).TrimEnd('/');
    }

    public async Task<IReadOnlyList<ConfluencePage>> GetPagesForIssueAsync(
        string issueKey,
        string? issueTitle = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(issueKey)) return Array.Empty<ConfluencePage>();

        var keywords = ExtractKeywords(issueTitle);
        var cql = BuildCql(issueKey, keywords);

        // Diagnostics: visible in Visual Studio's Output window (Debug pane).
        Debug.WriteLine($"[Confluence] Issue key      : {issueKey}");
        Debug.WriteLine($"[Confluence] Issue title    : {issueTitle ?? "<null>"}");
        Debug.WriteLine($"[Confluence] Extracted kw   : [{string.Join(", ", keywords)}]");
        Debug.WriteLine($"[Confluence] CQL            : {cql}");

        var url = $"wiki/rest/api/search?cql={Uri.EscapeDataString(cql)}" +
                  $"&limit={limit}" +
                  "&expand=content.history,content.version,content.space";

        Debug.WriteLine($"[Confluence] Relative URL   : {url}");

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);

        Debug.WriteLine($"[Confluence] Resolved URL   : {response.RequestMessage?.RequestUri}");
        Debug.WriteLine($"[Confluence] Status         : {(int)response.StatusCode} {response.ReasonPhrase}");

        if (!response.IsSuccessStatusCode)
        {
            // Surface the body — Atlassian usually returns useful structured errors
            // (e.g. {"message":"OAuth scope insufficient","code":403}).
            Debug.WriteLine($"[Confluence] Error body     : {rawJson}");
            return Array.Empty<ConfluencePage>();
        }

        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        var pages = new List<ConfluencePage>();
        if (!root.TryGetProperty("results", out var results))
        {
            Debug.WriteLine("[Confluence] Response had no 'results' field. Body follows:");
            Debug.WriteLine(rawJson);
            return pages;
        }

        var resultCount = results.GetArrayLength();
        Debug.WriteLine($"[Confluence] Result count   : {resultCount}");

        foreach (var item in results.EnumerateArray())
        {
            var page = MapResult(item);
            if (page is null)
            {
                Debug.WriteLine("[Confluence] Skipped a result (no content/id/title).");
                continue;
            }

            page.MatchReason = ExplainMatch(page, issueKey, keywords);
            Debug.WriteLine($"[Confluence]   → \"{page.Title}\" ({page.SpaceName})  [{page.MatchReason}]");
            pages.Add(page);
        }

        return pages;
    }

    /// <summary>
    /// Builds a CQL query that returns pages either mentioning the issue key
    /// verbatim or with significant keywords appearing in the title.
    /// </summary>
    private static string BuildCql(string issueKey, IReadOnlyList<string> keywords)
    {
        var clauses = new List<string>
        {
            $"text ~ \"{EscapeForCql(issueKey)}\""
        };

        if (keywords.Count > 0)
        {
            // Title-only match for topical relevance — broader, fewer false positives
            // than full-text keyword matches.
            var titleClauses = keywords.Select(k => $"title ~ \"{EscapeForCql(k)}\"");
            clauses.Add($"({string.Join(" OR ", titleClauses)})");
        }

        return $"({string.Join(" OR ", clauses)}) AND type IN (\"page\", \"blogpost\")";
    }

    /// <summary>
    /// Extracts up to 4 distinct, "significant" words from the issue summary:
    /// length ≥ 4, not a stopword, not a common backlog verb. Sorted longest-first
    /// because longer words tend to be rarer (rough IDF proxy).
    /// </summary>
    private static IReadOnlyList<string> ExtractKeywords(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return Array.Empty<string>();

        var tokens = title
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length >= 4)
            .Where(t => !Stopwords.Contains(t))
            .Where(t => !BacklogVerbs.Contains(t))
            .Where(t => !int.TryParse(t, out _)) // drop bare numbers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(t => t.Length)
            .Take(4)
            .ToList();

        return tokens;
    }

    /// <summary>
    /// Decides why a returned page is shown. Order of preference:
    /// 1. Explicit issue-key mention in the title → "Mentions &lt;key&gt;".
    /// 2. Title contains one or more keywords → "Title matches: a, b".
    /// 3. Otherwise body matched the key → "References &lt;key&gt;".
    /// </summary>
    private static string ExplainMatch(ConfluencePage page, string issueKey, IReadOnlyList<string> keywords)
    {
        if (!string.IsNullOrEmpty(page.Title)
            && page.Title.Contains(issueKey, StringComparison.OrdinalIgnoreCase))
            return $"Mentions {issueKey}";

        var hits = keywords
            .Where(k => page.Title.Contains(k, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (hits.Count > 0)
            return $"Title matches: {string.Join(", ", hits)}";

        return $"References {issueKey}";
    }

    private static readonly char[] WordSeparators =
    {
        ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']',
        '{', '}', '"', '\'', '/', '\\', '|', '-', '_', '+', '=', '*', '&', '<', '>'
    };

    /// <summary>Standard English stopwords plus a few common in user-story prose.</summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "above", "after", "again", "against", "also", "been", "before", "being",
        "below", "between", "both", "during", "each", "from", "further", "have", "having",
        "into", "more", "most", "other", "over", "same", "should", "some", "such", "than",
        "that", "their", "them", "there", "these", "they", "this", "those", "through",
        "under", "until", "very", "what", "when", "where", "which", "while", "with",
        "would", "your", "yours"
    };

    /// <summary>Verbs that appear in nearly every user-story title; not useful for matching.</summary>
    private static readonly HashSet<string> BacklogVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "create", "update", "delete", "remove", "implement", "integrate",
        "enable", "disable", "allow", "make", "build", "support", "introduce",
        "show", "hide", "display", "render", "fetch", "load", "save", "store",
        "fix", "refactor", "improve", "optimize", "optimise", "handle", "ensure",
        "feat", "feature", "bug", "task", "story", "spike", "chore"
    };

    private ConfluencePage? MapResult(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
            return null;

        var id = GetString(content, "id");
        var title = GetString(content, "title");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title)) return null;

        var page = new ConfluencePage
        {
            Id = id,
            Title = title,
            Type = GetString(content, "type"),
            WebUrl = ResolveAbsoluteUrl(GetString(result, "url")),
            LastModified = ParseDate(GetString(result, "lastModified"))
        };

        // Space metadata
        if (content.TryGetProperty("space", out var space) && space.ValueKind == JsonValueKind.Object)
        {
            page.SpaceKey = GetString(space, "key");
            page.SpaceName = GetString(space, "name");
        }
        // Fall back to the resultGlobalContainer if the content didn't include space details.
        if (string.IsNullOrEmpty(page.SpaceName)
            && result.TryGetProperty("resultGlobalContainer", out var container)
            && container.ValueKind == JsonValueKind.Object)
        {
            page.SpaceName = GetString(container, "title");
        }

        // Creator (history.createdBy)
        if (content.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Object)
        {
            page.CreatedAt = ParseDate(GetString(history, "createdDate"));
            if (history.TryGetProperty("createdBy", out var createdBy))
                page.Creator = MapContributor(createdBy, "Author");
        }

        // Last editor (version.by)
        if (content.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Object)
        {
            if (version.TryGetProperty("by", out var by))
                page.LastEditor = MapContributor(by, "Last edited");
            if (page.LastModified == DateTime.MinValue)
                page.LastModified = ParseDate(GetString(version, "when"));
        }

        return page;
    }

    private ConfluenceContributor? MapContributor(JsonElement user, string role)
    {
        if (user.ValueKind != JsonValueKind.Object) return null;

        var accountId = GetString(user, "accountId");
        var displayName = GetString(user, "displayName");
        if (string.IsNullOrEmpty(accountId) && string.IsNullOrEmpty(displayName)) return null;

        var avatar = string.Empty;
        if (user.TryGetProperty("profilePicture", out var profile) && profile.ValueKind == JsonValueKind.Object)
            avatar = ResolveAbsoluteUrl(GetString(profile, "path"));

        return new ConfluenceContributor
        {
            AccountId = accountId,
            DisplayName = displayName,
            AvatarUrl = avatar,
            Role = role
        };
    }

    /// <summary>Confluence returns relative URLs (e.g. "/wiki/spaces/ENG/...") — prepend the site host.</summary>
    private string ResolveAbsoluteUrl(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return raw;
        if (string.IsNullOrEmpty(_siteBaseUrl)) return raw;

        return raw.StartsWith('/')
            ? _siteBaseUrl + raw
            : $"{_siteBaseUrl}/{raw}";
    }

    private static string EscapeForCql(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;

    private static DateTime ParseDate(string raw) =>
        DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.MinValue;
}
