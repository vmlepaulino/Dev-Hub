using DevSprint.UI.Models;

namespace DevSprint.UI.Services;

public interface IJiraService
{
    Task<PagedResult<JiraIssue>> GetBacklogAsync(DateTime from, DateTime to, int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default);
    Task<PagedResult<JiraIssue>> GetMyIssuesAsync(DateTime from, DateTime to, int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default);
    Task<PagedResult<JiraIssue>> GetMyCommentedIssuesAsync(DateTime from, DateTime to, int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetCurrentSprintKeysAsync(CancellationToken cancellationToken = default);
}
