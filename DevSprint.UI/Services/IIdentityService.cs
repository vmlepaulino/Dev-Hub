using DevSprint.UI.Models;

namespace DevSprint.UI.Services;

public interface IIdentityService
{
    Task LoadAsync();
    Task SaveAsync();
    IReadOnlyList<TeamIdentity> GetAll();
    TeamIdentity? GetByJiraAccountId(string accountId);
    TeamIdentity? GetByGitHubUsername(string githubUsername);
    TeamIdentity? GetCurrentUser();
    void SetCurrentUser(TeamIdentity identity);
    void LinkGitHubUsername(TeamIdentity identity, string githubUsername);
    void MergeFromJira(IEnumerable<TeamIdentity> discovered);
    string ResolveDisplayName(string jiraDisplayName);
}
