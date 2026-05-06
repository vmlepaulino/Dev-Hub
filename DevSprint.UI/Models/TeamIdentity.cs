namespace DevSprint.UI.Models;

public sealed class TeamIdentity
{
    public string JiraAccountId { get; set; } = string.Empty;
    public string JiraDisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string GitHubUsername { get; set; } = string.Empty;
    public bool IsCurrentUser { get; set; }
}
