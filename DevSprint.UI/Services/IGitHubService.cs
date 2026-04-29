using DevSprint.UI.Models;

namespace DevSprint.UI.Services;

public interface IGitHubService
{
    Task<IReadOnlyList<GitHubPullRequest>> GetMyPullRequestsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitHubReview>> GetMyReviewsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitHubCommit>> GetMyCommitsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BranchInfo>> GetBranchesForIssueAsync(string issueKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeamMember>> GetContributorsForIssueAsync(string issueKey, CancellationToken cancellationToken = default);
}
