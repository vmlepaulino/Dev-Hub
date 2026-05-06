namespace DevSprint.UI.Models;

public sealed class TeamMember
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string GitHubUsername { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
