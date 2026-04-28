namespace DevSprint.UI.Models;

public sealed class GitHubReview
{
    public long Id { get; set; }
    public string Body { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public int PullRequestNumber { get; set; }
    public DateTime SubmittedAt { get; set; }
}
