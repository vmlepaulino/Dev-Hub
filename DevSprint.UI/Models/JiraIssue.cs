namespace DevSprint.UI.Models;

public sealed class JiraIssue
{
    public string Key { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public DateTime Updated { get; set; }
    public DateTime Created { get; set; }
    public string TimeSpent { get; set; } = string.Empty;
    public int DaysInCurrentState { get; set; }
    public bool HasDescription { get; set; }
    public bool HasAcceptanceCriteria { get; set; }
    public bool HasComments { get; set; }
    public int CommentCount { get; set; }
}
