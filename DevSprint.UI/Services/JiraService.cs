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

        var baseUrl = configuration["Jira:BaseUrl"]!.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(baseUrl);

        var email = configuration["Jira:Email"]!;
        var token = configuration["Jira:ApiToken"]!;
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _boardId = configuration["Jira:BoardId"]!;
        _projectKey = configuration["Jira:ProjectKey"]!;
        _baseUrl = baseUrl;
    }



    public async Task<PagedResult<JiraIssue>> GetBacklogAsync(DateTime from, DateTime to, int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");
        var jql = $"project = {_projectKey} AND updated >= \"{fromStr}\" AND updated <= \"{toStr}\" ORDER BY updated DESC";

        return await SearchIssuesAsync(jql, startAt, maxResults, cancellationToken);
    }

    public async Task<PagedResult<JiraIssue>> GetMyIssuesAsync(DateTime from, DateTime to, int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");
        var jql = $"assignee = currentUser() AND updated >= \"{fromStr}\" AND updated <= \"{toStr}\" ORDER BY created DESC";

        return await SearchIssuesAsync(jql, startAt, maxResults, cancellationToken);
    }

    public async Task<PagedResult<JiraIssue>> GetMyCommentedIssuesAsync(DateTime from, DateTime to, int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");
        var jql = $"comment ~ currentUser() AND updated >= \"{fromStr}\" AND updated <= \"{toStr}\" ORDER BY created DESC";

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

    private async Task<PagedResult<JiraIssue>> SearchIssuesAsync(string jql, int startAt, int maxResults, CancellationToken cancellationToken)
    {
        var fields = "summary,status,assignee,priority,issuetype,created,updated,timespent,statuscategorychangedate,description,comment,customfield_10037";
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
            CommentCount = GetCommentCount(fields)
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
}
