using DevSprint.UI.Models;

namespace DevSprint.UI.Services;

public interface IJiraService
{
    Task<IReadOnlyList<JiraIssue>> GetBacklogAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraIssue>> GetMyIssuesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraIssue>> GetMyCommentedIssuesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
