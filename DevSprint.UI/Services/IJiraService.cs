using DevSprint.UI.Models;

namespace DevSprint.UI.Services;

public interface IJiraService
{
    Task<PagedResult<JiraIssue>> GetProductBacklogAsync(int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default);
    Task<PagedResult<JiraIssue>> GetCurrentSprintIssuesAsync(int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default);
    Task<PagedResult<JiraIssue>> GetMyIssuesAsync(int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default);
    Task<PagedResult<JiraIssue>> GetMyCommentedIssuesAsync(int startAt = 0, int maxResults = 100, CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetCurrentSprintKeysAsync(CancellationToken cancellationToken = default);
    Task<SprintInfo?> GetActiveSprintInfoAsync(CancellationToken cancellationToken = default);
}
