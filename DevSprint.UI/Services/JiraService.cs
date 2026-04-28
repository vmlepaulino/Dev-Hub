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
    }

    public async Task<IReadOnlyList<JiraIssue>> GetBacklogAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");
        var jql = $"project = {_projectKey} AND sprint in openSprints() AND updated >= \"{fromStr}\" AND updated <= \"{toStr}\" ORDER BY created DESC";

        return await SearchIssuesAsync(jql, cancellationToken);
    }

    public async Task<IReadOnlyList<JiraIssue>> GetMyIssuesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");
        var jql = $"assignee = currentUser() AND updated >= \"{fromStr}\" AND updated <= \"{toStr}\" ORDER BY created DESC";

        return await SearchIssuesAsync(jql, cancellationToken);
    }

    public async Task<IReadOnlyList<JiraIssue>> GetMyCommentedIssuesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");
        // Standard Jira Cloud JQL — finds issues where current user added a comment in the date range
        var jql = $"comment ~ currentUser() AND updated >= \"{fromStr}\" AND updated <= \"{toStr}\" ORDER BY created DESC";

        return await SearchIssuesAsync(jql, cancellationToken);
    }

    private async Task<IReadOnlyList<JiraIssue>> SearchIssuesAsync(string jql, CancellationToken cancellationToken)
    {
        var issues = new List<JiraIssue>();
        var startAt = 0;
        const int maxResults = 50;

        while (true)
        {
            var fields = "summary,status,assignee,priority,issuetype,created,updated,timespent,statuscategorychangedate";
            var url = $"rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&fields={fields}&startAt={startAt}&maxResults={maxResults}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("issues", out var issuesArray))
                break;

            foreach (var item in issuesArray.EnumerateArray())
            {
                issues.Add(MapIssue(item));
            }

            if (!root.TryGetProperty("total", out var totalElement) || startAt + maxResults >= totalElement.GetInt32())
                break;

            startAt += maxResults;
        }

        return issues;
    }

    private static JiraIssue MapIssue(JsonElement issue)
    {
        if (!issue.TryGetProperty("fields", out var fields))
            return new JiraIssue { Key = GetStringOrDefault(issue, "key") };

        return new JiraIssue
        {
            Key = GetStringOrDefault(issue, "key"),
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
            DaysInCurrentState = GetDaysInState(fields)
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

    private static int GetDaysInState(JsonElement fields)
    {
        var stateChangeDate = GetDateOrDefault(fields, "statuscategorychangedate");
        return stateChangeDate > DateTime.MinValue
            ? (int)(DateTime.UtcNow - stateChangeDate.ToUniversalTime()).TotalDays
            : 0;
    }

    private static string FormatSeconds(int totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }
}
