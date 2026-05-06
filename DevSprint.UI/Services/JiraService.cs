using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevSprint.UI.Models;
using Microsoft.Extensions.Configuration;

namespace DevSprint.UI.Services;

public sealed class JiraService : IJiraService
{
    private readonly HttpClient _httpClient;
    private readonly string _boardId;
    private readonly string _projectKey;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public JiraService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        // OAuth 2.0 (3LO) routes Jira API calls through api.atlassian.com.
        // JiraApiBaseUriHandler rewrites paths to /ex/jira/{cloudId}/...
        // JiraBearerTokenHandler attaches the Authorization header.
        // The constructor must NOT touch DefaultRequestHeaders.Authorization.
        _httpClient.BaseAddress = new Uri("https://api.atlassian.com/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // BrowseUrl is user-facing (e.g. https://your-site.atlassian.net/browse/PROJ-123)
        // and stays on the configured site URL — that part of the API hasn't moved.
        _baseUrl = configuration["Jira:BaseUrl"]!.TrimEnd('/') + "/";

        _boardId = configuration["Jira:BoardId"]!;
        _projectKey = configuration["Jira:ProjectKey"]!;
    }




    public async Task<PagedResult<JiraIssue>> GetProductBacklogAsync(int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        var jql = $"project = {_projectKey} AND sprint not in openSprints() AND statusCategory != Done ORDER BY updated DESC";
        return await SearchIssuesAsync(jql, startAt, maxResults, cancellationToken);
    }

    public async Task<PagedResult<JiraIssue>> GetCurrentSprintIssuesAsync(int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        var jql = $"project = {_projectKey} AND sprint in openSprints() ORDER BY status ASC, updated DESC";
        return await SearchIssuesAsync(jql, startAt, maxResults, cancellationToken);
    }

    public async Task<PagedResult<JiraIssue>> GetMyIssuesAsync(int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        var jql = $"project = {_projectKey} AND assignee = currentUser() AND statusCategory != Done ORDER BY sprint DESC, updated DESC";
        return await SearchIssuesAsync(jql, startAt, maxResults, cancellationToken);
    }

    public async Task<PagedResult<JiraIssue>> GetMyCommentedIssuesAsync(int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        var jql = $"project = {_projectKey} AND assignee != currentUser() AND statusCategory != Done ORDER BY updated DESC";
        return await SearchIssuesAsync(jql, startAt, maxResults, cancellationToken);
    }



    public async Task<HashSet<string>> GetCurrentSprintKeysAsync(CancellationToken cancellationToken = default)
    {
        var keys = new HashSet<string>();
        var jql = $"project = {_projectKey} AND sprint in openSprints()";
        var startAt = 0;

        while (true)
        {
            var url = $"rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&fields=key&startAt={startAt}&maxResults=100";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) break;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("issues", out var issues)) break;

            foreach (var issue in issues.EnumerateArray())
            {
                var key = GetStringOrDefault(issue, "key");
                if (!string.IsNullOrEmpty(key))
                    keys.Add(key);
            }

            var total = root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
            startAt += 100;
            if (total == 0 || startAt >= total) break;
        }

        return keys;
    }

    public async Task<JiraIssue?> GetIssueByKeyAsync(string issueKey, CancellationToken cancellationToken = default)
    {
        var jql = $"key = {issueKey}";
        var result = await SearchIssuesAsync(jql, 0, 1, cancellationToken);
        return result.Items.Count > 0 ? result.Items[0] : null;
    }

    private async Task<PagedResult<JiraIssue>> SearchIssuesAsync(string jql, int startAt, int maxResults, CancellationToken cancellationToken)
    {
        var fields = "summary,status,assignee,priority,issuetype,created,updated,timespent,statuscategorychangedate,description,comment,customfield_10037,story_points";
        var url = $"rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&fields={fields}&expand=changelog&startAt={startAt}&maxResults={maxResults}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var issues = new List<JiraIssue>();

        if (root.TryGetProperty("issues", out var issuesArray))
        {
            foreach (var item in issuesArray.EnumerateArray())
            {
                issues.Add(MapIssue(item, _baseUrl));
            }
        }

        // Try 'total', fall back to checking for next page indicators
        var total = 0;
        if (root.TryGetProperty("total", out var totalElement) && totalElement.ValueKind == JsonValueKind.Number)
        {
            total = totalElement.GetInt32();
        }
        else
        {
            // If no total, estimate: if we got a full page, assume there's more
            total = issues.Count == maxResults ? startAt + issues.Count + 1 : startAt + issues.Count;
        }

        return new PagedResult<JiraIssue>
        {
            Items = issues,
            Total = total,
            StartAt = startAt
        };
    }


    private static JiraIssue MapIssue(JsonElement issue, string baseUrl)
    {
        var key = GetStringOrDefault(issue, "key");

        if (!issue.TryGetProperty("fields", out var fields))
            return new JiraIssue { Key = key, BrowseUrl = $"{baseUrl}browse/{key}" };

        return new JiraIssue
        {
            Key = key,
            BrowseUrl = $"{baseUrl}browse/{key}",
            Summary = GetStringOrDefault(fields, "summary"),
            Status = GetNestedStringOrDefault(fields, "status", "name"),
            Assignee = GetNestedStringOrDefault(fields, "assignee", "displayName"),
            AssigneeAccountId = GetNestedStringOrDefault(fields, "assignee", "accountId"),
            AssigneeAvatarUrl = GetAvatarUrl(fields),
            Priority = GetNestedStringOrDefault(fields, "priority", "name"),
            IssueType = GetNestedStringOrDefault(fields, "issuetype", "name"),
            Created = GetDateOrDefault(fields, "created"),
            Updated = GetDateOrDefault(fields, "updated"),
            TimeSpent = fields.TryGetProperty("timespent", out var timeSpent) && timeSpent.ValueKind == JsonValueKind.Number
                ? FormatSeconds(timeSpent.GetInt32())
                : string.Empty,
            DaysInCurrentState = GetDaysInState(fields),
            StateHistory = BuildStateHistory(issue, fields),
            HasDescription = HasContent(fields, "description"),
            HasAcceptanceCriteria = HasContent(fields, "customfield_10037"),
            HasComments = GetCommentCount(fields) > 0,
            CommentCount = GetCommentCount(fields),
            StoryPoints = GetDoubleOrDefault(fields, "story_points")
        };
    }

    private static string GetStringOrDefault(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string GetNestedStringOrDefault(JsonElement element, string propertyName, string nestedPropertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null
            && prop.TryGetProperty(nestedPropertyName, out var nested) && nested.ValueKind == JsonValueKind.String
            ? nested.GetString() ?? string.Empty
            : string.Empty;
    }

    private static DateTime GetDateOrDefault(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            && DateTime.TryParse(prop.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : DateTime.MinValue;
    }

    private static double GetDoubleOrDefault(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return 0;
        return prop.ValueKind == JsonValueKind.Number ? prop.GetDouble() : 0;
    }

    public async Task<SprintInfo?> GetActiveSprintInfoAsync(CancellationToken cancellationToken = default)
    {
        var sprints = await GetBoardSprintsAsync("active", cancellationToken);
        return sprints.Count > 0 ? sprints[0] : null;
    }

    public async Task<IReadOnlyList<SprintInfo>> GetSprintsForQuarterAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.Today;
        var quarterStart = new DateTime(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1);

        var allSprints = new List<SprintInfo>();
        allSprints.AddRange(await GetBoardSprintsAsync("active", cancellationToken));
        allSprints.AddRange(await GetBoardSprintsAsync("closed", cancellationToken));

        return allSprints
            .Where(s => s.StartDate >= quarterStart || s.EndDate >= quarterStart)
            .OrderByDescending(s => s.StartDate)
            .ToList();
    }

    public async Task<PagedResult<JiraIssue>> GetSprintIssuesAsync(int sprintId, int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        var jql = $"project = {_projectKey} AND sprint = {sprintId} ORDER BY status ASC, updated DESC";
        return await SearchIssuesAsync(jql, startAt, maxResults, cancellationToken);
    }

    private async Task<List<SprintInfo>> GetBoardSprintsAsync(string state, CancellationToken cancellationToken)
    {
        var sprints = new List<SprintInfo>();
        var startAt = 0;

        while (true)
        {
            var url = $"rest/agile/1.0/board/{_boardId}/sprint?state={state}&startAt={startAt}&maxResults=50";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) break;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("values", out var values)) break;

            foreach (var sprint in values.EnumerateArray())
            {
                sprints.Add(new SprintInfo
                {
                    Id = sprint.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : 0,
                    Name = GetStringOrDefault(sprint, "name"),
                    State = GetStringOrDefault(sprint, "state"),
                    StartDate = GetNullableDateOrDefault(sprint, "startDate"),
                    EndDate = GetNullableDateOrDefault(sprint, "endDate")
                });
            }

            var isLast = doc.RootElement.TryGetProperty("isLast", out var last) && last.GetBoolean();
            if (isLast || values.GetArrayLength() < 50) break;
            startAt += 50;
        }

        return sprints;
    }

    private static DateTime? GetNullableDateOrDefault(JsonElement element, string propertyName)
    {
        var date = GetDateOrDefault(element, propertyName);
        return date > DateTime.MinValue ? date : null;
    }

    private static bool HasContent(JsonElement fields, string propertyName)
    {
        if (!fields.TryGetProperty(propertyName, out var prop))
            return false;

        return prop.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(prop.GetString()),
            JsonValueKind.Object => true,
            JsonValueKind.Null => false,
            _ => false
        };
    }

    private static int GetCommentCount(JsonElement fields)
    {
        if (!fields.TryGetProperty("comment", out var comment) || comment.ValueKind != JsonValueKind.Object)
            return 0;

        if (comment.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
            return total.GetInt32();

        if (comment.TryGetProperty("comments", out var comments) && comments.ValueKind == JsonValueKind.Array)
            return comments.GetArrayLength();

        return 0;
    }

    private static int GetDaysInState(JsonElement fields)
    {
        var stateChangeDate = GetDateOrDefault(fields, "statuscategorychangedate");
        return stateChangeDate > DateTime.MinValue
            ? (int)(DateTime.UtcNow - stateChangeDate.ToUniversalTime()).TotalDays
            : 0;
    }

    private static List<StateTransition> BuildStateHistory(JsonElement issue, JsonElement fields)
    {
        var transitions = new List<StateTransition>();

        if (!issue.TryGetProperty("changelog", out var changelog) ||
            !changelog.TryGetProperty("histories", out var histories))
            return transitions;

        // Collect all status changes sorted by date
        var statusChanges = new List<(string FromStatus, string ToStatus, DateTime Date)>();

        foreach (var history in histories.EnumerateArray())
        {
            var historyDate = GetDateOrDefault(history, "created");
            if (historyDate == DateTime.MinValue) continue;

            if (!history.TryGetProperty("items", out var items)) continue;

            foreach (var item in items.EnumerateArray())
            {
                var fieldName = GetStringOrDefault(item, "field");
                if (!string.Equals(fieldName, "status", StringComparison.OrdinalIgnoreCase)) continue;

                var fromStatus = GetStringOrDefault(item, "fromString");
                var toStatus = GetStringOrDefault(item, "toString");

                statusChanges.Add((fromStatus, toStatus, historyDate));
            }
        }

        if (statusChanges.Count == 0)
            return transitions;

        statusChanges.Sort((a, b) => a.Date.CompareTo(b.Date));

        // Calculate days in each state
        for (var i = 0; i < statusChanges.Count; i++)
        {
            var current = statusChanges[i];
            var nextDate = i + 1 < statusChanges.Count
                ? statusChanges[i + 1].Date
                : DateTime.UtcNow;

            transitions.Add(new StateTransition
            {
                FromStatus = current.FromStatus,
                ToStatus = current.ToStatus,
                TransitionDate = current.Date,
                DaysInState = (int)(nextDate - current.Date).TotalDays
            });
        }

        return transitions;
    }

    private static string FormatSeconds(int totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }

    private static string GetAvatarUrl(JsonElement fields)
    {
        if (!fields.TryGetProperty("assignee", out var assignee) || assignee.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (!assignee.TryGetProperty("avatarUrls", out var urls) || urls.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (urls.TryGetProperty("48x48", out var url48) && url48.ValueKind == JsonValueKind.String)
            return url48.GetString() ?? string.Empty;
        if (urls.TryGetProperty("32x32", out var url32) && url32.ValueKind == JsonValueKind.String)
            return url32.GetString() ?? string.Empty;

        return string.Empty;
    }

    public async Task<TeamIdentity?> GetMyselfAsync(CancellationToken cancellationToken = default)
    {
        var url = "rest/api/3/myself";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var avatarUrl = string.Empty;
        if (root.TryGetProperty("avatarUrls", out var urls) && urls.ValueKind == JsonValueKind.Object)
        {
            if (urls.TryGetProperty("48x48", out var url48) && url48.ValueKind == JsonValueKind.String)
                avatarUrl = url48.GetString() ?? string.Empty;
        }

        return new TeamIdentity
        {
            JiraAccountId = GetStringOrDefault(root, "accountId"),
            JiraDisplayName = GetStringOrDefault(root, "displayName"),
            DisplayName = GetStringOrDefault(root, "displayName"),
            Email = GetStringOrDefault(root, "emailAddress"),
            AvatarUrl = avatarUrl
        };
    }

    public async Task<IReadOnlyList<TeamIdentity>> GetBoardMembersAsync(CancellationToken cancellationToken = default)
    {
        var members = new Dictionary<string, TeamIdentity>(StringComparer.OrdinalIgnoreCase);

        // Get all assignees from recent sprint issues
        var jql = $"project = {_projectKey} AND sprint in openSprints() OR (project = {_projectKey} AND sprint in closedSprints() AND updated >= -90d)";
        var startAt = 0;

        while (true)
        {
            var fieldsParam = "assignee";
            var searchUrl = $"rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&fields={fieldsParam}&startAt={startAt}&maxResults=100";
            using var response = await _httpClient.GetAsync(searchUrl, cancellationToken);
            if (!response.IsSuccessStatusCode) break;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("issues", out var issues)) break;

            foreach (var issue in issues.EnumerateArray())
            {
                if (!issue.TryGetProperty("fields", out var fields)) continue;
                if (!fields.TryGetProperty("assignee", out var assignee) || assignee.ValueKind != JsonValueKind.Object) continue;

                var accountId = GetStringOrDefault(assignee, "accountId");
                if (string.IsNullOrEmpty(accountId) || members.ContainsKey(accountId)) continue;

                var avatarUrl = string.Empty;
                if (assignee.TryGetProperty("avatarUrls", out var urls) && urls.ValueKind == JsonValueKind.Object)
                {
                    if (urls.TryGetProperty("48x48", out var url48) && url48.ValueKind == JsonValueKind.String)
                        avatarUrl = url48.GetString() ?? string.Empty;
                }

                members[accountId] = new TeamIdentity
                {
                    JiraAccountId = accountId,
                    JiraDisplayName = GetStringOrDefault(assignee, "displayName"),
                    Email = GetStringOrDefault(assignee, "emailAddress"),
                    AvatarUrl = avatarUrl
                };
            }

            var total = root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
            startAt += 100;
            if (total == 0 || startAt >= total) break;
        }

        return members.Values.ToList();
    }
}
