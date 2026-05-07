namespace DevSprint.UI.Models;

/// <summary>
/// A person tied to a Confluence page by some role (creator, last editor, commenter, etc).
/// AccountId is the Atlassian-wide identifier, identical to the assignee accountId
/// returned by Jira — that's what makes cross-product team-member matching trivial.
/// </summary>
public sealed class ConfluenceContributor
{
    /// <summary>Atlassian account id — same value Jira uses for the same person.</summary>
    public string AccountId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>Role on the page: "Author", "Last edited by", "Commenter", etc.</summary>
    public string Role { get; set; } = string.Empty;
}
