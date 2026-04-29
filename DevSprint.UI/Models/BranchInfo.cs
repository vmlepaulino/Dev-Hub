namespace DevSprint.UI.Models;

public sealed class BranchInfo
{
    public string Name { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string LastCommitSha { get; set; } = string.Empty;
    public string LastCommitAuthor { get; set; } = string.Empty;
    public DateTime LastCommitDate { get; set; }
}
