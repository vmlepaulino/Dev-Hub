using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DevSprint.UI.Models;
using Microsoft.Extensions.Configuration;

namespace DevSprint.UI.Services;

public sealed class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly string _username;
    private readonly string _organization;
    private readonly string[] _repositories;

    public GitHubService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.github.com/");

        var token = configuration["GitHub:ApiToken"]!;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DevSprint", "1.0"));

        _username = configuration["GitHub:Username"]!;
        _organization = configuration["GitHub:Organization"]!;
        _repositories = configuration.GetSection("GitHub:Repositories").Get<string[]>() ?? [];
    }

    public async Task<IReadOnlyList<GitHubPullRequest>> GetMyPullRequestsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var pullRequests = new List<GitHubPullRequest>();
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");

        // Search API covers all repos at once
        var page = 1;
        while (true)
        {
            var repoQualifiers = string.Join("+", _repositories.Select(r => $"repo:{_organization}/{r}"));
            var query = $"author:{_username}+type:pr+created:{fromStr}..{toStr}+{repoQualifiers}";
            var url = $"search/issues?q={query}&per_page=100&page={page}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("items", out var items))
                break;

            foreach (var item in items.EnumerateArray())
            {
                pullRequests.Add(new GitHubPullRequest
                {
                    Number = item.TryGetProperty("number", out var num) ? num.GetInt32() : 0,
                    Title = GetString(item, "title"),
                    State = GetString(item, "state"),
                    Url = GetString(item, "html_url"),
                    Repository = ExtractRepoName(GetString(item, "repository_url")),
                    CreatedAt = GetDate(item, "created_at"),
                    MergedAt = item.TryGetProperty("pull_request", out var pr) &&
                               pr.TryGetProperty("merged_at", out var merged) &&
                               merged.ValueKind == JsonValueKind.String &&
                               DateTime.TryParse(merged.GetString(), out var mergedDate)
                        ? mergedDate
                        : null,
                    ClosedAt = item.TryGetProperty("closed_at", out var closed) &&
                               closed.ValueKind == JsonValueKind.String &&
                               DateTime.TryParse(closed.GetString(), out var closedDate)
                        ? closedDate
                        : null
                });
            }

            if (!root.TryGetProperty("total_count", out var totalCount) || page * 100 >= totalCount.GetInt32())
                break;
            page++;
        }

        return pullRequests;
    }

    public async Task<IReadOnlyList<GitHubReview>> GetMyReviewsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var reviews = new List<GitHubReview>();

        // For each repo, get PRs reviewed by user, then fetch reviews
        foreach (var repo in _repositories)
        {
            var fromStr = from.ToString("yyyy-MM-dd");
            var toStr = to.ToString("yyyy-MM-dd");
            var repoQualifier = $"repo:{_organization}/{repo}";
            var query = $"reviewed-by:{_username}+type:pr+updated:{fromStr}..{toStr}+{repoQualifier}";
            var url = $"search/issues?q={query}&per_page=100";

            using var searchResponse = await _httpClient.GetAsync(url, cancellationToken);
            searchResponse.EnsureSuccessStatusCode();

            var searchJson = await searchResponse.Content.ReadAsStringAsync(cancellationToken);
            using var searchDoc = JsonDocument.Parse(searchJson);

            if (!searchDoc.RootElement.TryGetProperty("items", out var searchItems))
                continue;

            foreach (var item in searchItems.EnumerateArray())
            {
                if (!item.TryGetProperty("number", out var numEl)) continue;
                var prNumber = numEl.GetInt32();
                var reviewsUrl = $"repos/{_organization}/{repo}/pulls/{prNumber}/reviews?per_page=100";

                using var reviewResponse = await _httpClient.GetAsync(reviewsUrl, cancellationToken);
                if (!reviewResponse.IsSuccessStatusCode) continue;

                var reviewJson = await reviewResponse.Content.ReadAsStringAsync(cancellationToken);
                using var reviewDoc = JsonDocument.Parse(reviewJson);

                foreach (var review in reviewDoc.RootElement.EnumerateArray())
                {
                    var userLogin = review.TryGetProperty("user", out var userEl) ? GetString(userEl, "login") : string.Empty;
                    if (!string.Equals(userLogin, _username, StringComparison.OrdinalIgnoreCase)) continue;

                    var submittedAt = GetDate(review, "submitted_at");
                    if (submittedAt < from || submittedAt > to) continue;

                    reviews.Add(new GitHubReview
                    {
                        Id = review.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0,
                        Body = review.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
                            ? body.GetString() ?? string.Empty
                            : string.Empty,
                        State = GetString(review, "state"),
                        Repository = repo,
                        PullRequestNumber = prNumber,
                        SubmittedAt = submittedAt
                    });
                }
            }
        }

        return reviews;
    }

    public async Task<IReadOnlyList<GitHubCommit>> GetMyCommitsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        var commits = new List<GitHubCommit>();
        var sinceStr = from.ToUniversalTime().ToString("o");
        var untilStr = to.ToUniversalTime().ToString("o");

        foreach (var repo in _repositories)
        {
            var page = 1;
            while (true)
            {
                var url = $"repos/{_organization}/{repo}/commits?author={_username}&since={sinceStr}&until={untilStr}&per_page=100&page={page}";
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) break;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var items = doc.RootElement;

                if (items.GetArrayLength() == 0) break;

                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("commit", out var commit)) continue;

                    commits.Add(new GitHubCommit
                    {
                        Sha = GetString(item, "sha"),
                        Message = GetString(commit, "message"),
                        Repository = repo,
                        Url = GetString(item, "html_url"),
                        Date = commit.TryGetProperty("author", out var author) ? GetDate(author, "date") : DateTime.MinValue
                    });
                }

                if (items.GetArrayLength() < 100) break;
                page++;
            }
        }

        return commits;
    }

    private static string ExtractRepoName(string repositoryUrl)
    {
        var lastSlash = repositoryUrl.LastIndexOf('/');
        return lastSlash >= 0 ? repositoryUrl[(lastSlash + 1)..] : repositoryUrl;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;
    }

    private static DateTime GetDate(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            && DateTime.TryParse(prop.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : DateTime.MinValue;
    }
}
