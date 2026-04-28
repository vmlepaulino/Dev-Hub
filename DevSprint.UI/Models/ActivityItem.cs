namespace DevSprint.UI.Models;

public enum ActivitySource
{
    Jira,
    GitHub
}

public enum ActivityType
{
    JiraIssueUpdate,
    PullRequest,
    Review,
    Comment,
    Commit,
    Merge
}

public sealed class ActivityItem
{
    public ActivitySource Source { get; set; }
    public ActivityType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}
