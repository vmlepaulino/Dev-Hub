using DevSprint.UI.Models;

namespace DevSprint.UI.Services;

public interface IIdentityService
{
    Task LoadAsync();
    Task SaveAsync();
    IReadOnlyList<TeamIdentity> GetAll();
    TeamIdentity? GetByJiraAccountId(string accountId);
    TeamIdentity? GetCurrentUser();
    void SetCurrentUser(TeamIdentity identity);
    void MergeFromJira(IEnumerable<TeamIdentity> discovered);
    string ResolveDisplayName(string jiraDisplayName);
}
