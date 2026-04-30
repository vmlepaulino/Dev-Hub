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
            var repoQualifiers = string.Join(" ", _repositories.Select(r => $"repo:{_organization}/{r}"));
            var query = $"author:{_username} type:pr created:{fromStr}..{toStr} {repoQualifiers}";
            var url = $"search/issues?q={Uri.EscapeDataString(query)}&per_page=100&page={page}";

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
            var query = $"reviewed-by:{_username} type:pr updated:{fromStr}..{toStr} {repoQualifier}";
            var url = $"search/issues?q={Uri.EscapeDataString(query)}&per_page=100";

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

    public async Task<IReadOnlyList<BranchInfo>> GetBranchesForIssueAsync(string issueKey, DateTime? since = null, CancellationToken cancellationToken = default)
    {
        var branches = new Dictionary<string, BranchInfo>(StringComparer.OrdinalIgnoreCase);
        var sinceParam = since.HasValue ? $"&since={since.Value.ToUniversalTime():o}" : string.Empty;

        // Strategy 1: List PRs sorted by newest, filtered by since date
        foreach (var repo in _repositories)
        {
            try
            {
                var page = 1;
                while (true)
                {
                    var url = $"repos/{_organization}/{repo}/pulls?state=all&sort=updated&direction=desc&per_page=100&page={page}";
                    using var response = await _httpClient.GetAsync(url, cancellationToken);
                    if (!response.IsSuccessStatusCode) break;

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    var array = doc.RootElement;

                    if (array.GetArrayLength() == 0) break;

                    var reachedOlderThanSince = false;
                    foreach (var pr in array.EnumerateArray())
                    {
                        var updatedAt = GetDate(pr, "updated_at");
                        if (since.HasValue && updatedAt < since.Value)
                        {
                            reachedOlderThanSince = true;
                            break;
                        }

                        var title = GetString(pr, "title");
                        var branchName = pr.TryGetProperty("head", out var head) ? GetString(head, "ref") : string.Empty;

                        if (string.IsNullOrEmpty(branchName)) continue;
                        if (!title.Contains(issueKey, StringComparison.OrdinalIgnoreCase)
                            && !branchName.Contains(issueKey, StringComparison.OrdinalIgnoreCase)) continue;

                        if (branches.ContainsKey(branchName)) continue;

                        var commitSha = pr.TryGetProperty("head", out var h) ? GetString(h, "sha") : string.Empty;
                        if (commitSha.Length > 7) commitSha = commitSha[..7];

                        var prAuthor = pr.TryGetProperty("user", out var prUser) ? GetString(prUser, "login") : string.Empty;

                        branches[branchName] = new BranchInfo
                        {
                            Name = branchName,
                            Repository = repo,
                            LastCommitSha = commitSha,
                            LastCommitAuthor = prAuthor,
                            LastCommitDate = updatedAt
                        };
                    }

                    if (reachedOlderThanSince || array.GetArrayLength() < 100) break;
                    page++;
                }
            }
            catch { /* skip repo on error */ }
        }

        // Strategy 2: List branches and match by issue key pattern (contains)
        foreach (var repo in _repositories)
        {
            var page = 1;
            while (true)
            {
                var url = $"repos/{_organization}/{repo}/branches?per_page=100&page={page}";
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) break;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var array = doc.RootElement;

                foreach (var branchEl in array.EnumerateArray())
                {
                    var name = GetString(branchEl, "name");
                    if (branches.ContainsKey(name)) continue;
                    if (!name.Contains(issueKey, StringComparison.OrdinalIgnoreCase)) continue;

                    var branch = await GetBranchInfoFromElement(repo, name, branchEl, cancellationToken);
                    branches[name] = branch;
                }

                if (array.GetArrayLength() < 100) break;
                page++;
            }
        }

        return branches.Values.ToList();
    }

    private async Task<BranchInfo?> GetBranchInfoAsync(string repo, string branchName, CancellationToken cancellationToken)
    {
        var url = $"repos/{_organization}/{repo}/branches/{Uri.EscapeDataString(branchName)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return await GetBranchInfoFromElement(repo, branchName, doc.RootElement, cancellationToken);
    }

    private async Task<BranchInfo> GetBranchInfoFromElement(string repo, string branchName, JsonElement branchEl, CancellationToken cancellationToken)
    {
        var commitSha = string.Empty;
        var commitDate = DateTime.MinValue;
        var commitAuthor = string.Empty;

        if (branchEl.TryGetProperty("commit", out var commit))
        {
            commitSha = GetString(commit, "sha");
            if (commitSha.Length > 7) commitSha = commitSha[..7];

            var commitUrl = GetString(commit, "url");
            if (!string.IsNullOrEmpty(commitUrl))
            {
                var relativeUrl = commitUrl.Replace("https://api.github.com/", "");
                using var commitResponse = await _httpClient.GetAsync(relativeUrl, cancellationToken);
                if (commitResponse.IsSuccessStatusCode)
                {
                    var commitJson = await commitResponse.Content.ReadAsStringAsync(cancellationToken);
                    using var commitDoc = JsonDocument.Parse(commitJson);
                    if (commitDoc.RootElement.TryGetProperty("commit", out var commitData)
                        && commitData.TryGetProperty("author", out var author))
                    {
                        commitAuthor = GetString(author, "name");
                        commitDate = GetDate(author, "date");
                    }
                }
            }
        }

        return new BranchInfo
        {
            Name = branchName,
            Repository = repo,
            LastCommitSha = commitSha,
            LastCommitAuthor = commitAuthor,
            LastCommitDate = commitDate
        };
    }

    public async Task<IReadOnlyList<TeamMember>> GetContributorsForIssueAsync(string issueKey, DateTime? since = null, CancellationToken cancellationToken = default)
    {
        var members = new Dictionary<string, TeamMember>(StringComparer.OrdinalIgnoreCase);

        foreach (var repo in _repositories)
        {
            try
            {
                var page = 1;
                while (true)
                {
                    var url = $"repos/{_organization}/{repo}/pulls?state=all&sort=updated&direction=desc&per_page=100&page={page}";
                    using var response = await _httpClient.GetAsync(url, cancellationToken);
                    if (!response.IsSuccessStatusCode) break;

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    var array = doc.RootElement;

                    if (array.GetArrayLength() == 0) break;

                    var reachedOlderThanSince = false;
                    foreach (var pr in array.EnumerateArray())
                    {
                        var updatedAt = GetDate(pr, "updated_at");
                        if (since.HasValue && updatedAt < since.Value)
                        {
                            reachedOlderThanSince = true;
                            break;
                        }

                        var title = GetString(pr, "title");
                        var branchName = pr.TryGetProperty("head", out var head) ? GetString(head, "ref") : string.Empty;

                        if (!title.Contains(issueKey, StringComparison.OrdinalIgnoreCase)
                            && !branchName.Contains(issueKey, StringComparison.OrdinalIgnoreCase)) continue;

                        // PR author = contributing
                        if (pr.TryGetProperty("user", out var user))
                        {
                            var login = GetString(user, "login");
                            if (!string.IsNullOrEmpty(login) && !members.ContainsKey(login))
                            {
                                members[login] = new TeamMember
                                {
                                    Name = login,
                                    AvatarUrl = GetString(user, "avatar_url"),
                                    Role = "Contributing"
                                };
                            }
                        }

                        // Assignees = reviewers
                        if (pr.TryGetProperty("assignees", out var assignees))
                        {
                            foreach (var assignee in assignees.EnumerateArray())
                            {
                                var login = GetString(assignee, "login");
                                if (!string.IsNullOrEmpty(login) && !members.ContainsKey(login))
                                {
                                    members[login] = new TeamMember
                                    {
                                        Name = login,
                                        AvatarUrl = GetString(assignee, "avatar_url"),
                                        Role = "Reviewer"
                                    };
                                }
                            }
                        }

                        // Requested reviewers
                        if (pr.TryGetProperty("requested_reviewers", out var reviewers))
                        {
                            foreach (var reviewer in reviewers.EnumerateArray())
                            {
                                var login = GetString(reviewer, "login");
                                if (!string.IsNullOrEmpty(login) && !members.ContainsKey(login))
                                {
                                    members[login] = new TeamMember
                                    {
                                        Name = login,
                                        AvatarUrl = GetString(reviewer, "avatar_url"),
                                        Role = "Reviewer"
                                    };
                                }
                            }
                        }
                    }

                    if (reachedOlderThanSince || array.GetArrayLength() < 100) break;
                    page++;
                }
            }
            catch { /* skip repo on error */ }
        }

        return members.Values.ToList();
    }
}
