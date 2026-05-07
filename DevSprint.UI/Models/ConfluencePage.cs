namespace DevSprint.UI.Models;

/// <summary>
/// A Confluence page or blog post that mentions a given Jira issue key.
/// Returned by <see cref="Services.IConfluenceService.GetPagesForIssueAsync"/>.
/// </summary>
public sealed class ConfluencePage
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>"page" or "blogpost" — Confluence treats both as searchable content.</summary>
    public string Type { get; set; } = "page";

    /// <summary>Human-friendly space name (e.g. "Engineering").</summary>
    public string SpaceName { get; set; } = string.Empty;

    /// <summary>Space key (e.g. "ENG").</summary>
    public string SpaceKey { get; set; } = string.Empty;

    /// <summary>Absolute URL the user can open in their browser.</summary>
    public string WebUrl { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }

    /// <summary>The original author of the page.</summary>
    public ConfluenceContributor? Creator { get; set; }

    /// <summary>The most recent editor (may be the same person as the creator).</summary>
    public ConfluenceContributor? LastEditor { get; set; }

    /// <summary>
    /// Human-readable explanation of why this page surfaced for the current issue.
    /// Examples: "Mentions ENG-123", "Title matches: OAuth, login".
    /// </summary>
    public string MatchReason { get; set; } = string.Empty;
}
