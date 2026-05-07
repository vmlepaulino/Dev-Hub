using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevSprint.UI.Models;

namespace DevSprint.UI.ViewModels;

/// <summary>
/// Unified person row shown in the sidebar's "People" tab. Combines individuals
/// surfaced from multiple sources (Jira assignee, GitHub PR contributors,
/// Confluence page authors / editors / commenters) into a single de-duplicated
/// entry, tagged with one or more roles to indicate why they appear.
/// </summary>
/// <remarks>
/// Identity matching across sources uses, in priority order:
/// AtlassianAccountId → GitHubUsername → Email → DisplayName.
/// Atlassian account id is the strongest match — Jira and Confluence share the
/// same accountId namespace for the same person.
/// </remarks>
public sealed partial class SidebarPersonViewModel : ObservableObject
{
    public const string TagTeamMember = "Team member";
    public const string TagKnowledgeContributor = "Knowledge contributor";

    public string DisplayName { get; }
    public string Email { get; }
    public string AvatarUrl { get; }

    /// <summary>Atlassian-wide account id (same across Jira and Confluence). Empty for GitHub-only contributors.</summary>
    public string AtlassianAccountId { get; }

    /// <summary>GitHub login. Empty for people surfaced from Atlassian only.</summary>
    public string GitHubUsername { get; }

    /// <summary>
    /// The original <see cref="TeamMember"/> projection (if surfaced via the Jira/GitHub
    /// path) — kept so existing Teams-chat and link-GitHub commands keep working
    /// without rewriting their parameter types.
    /// </summary>
    public TeamMember? UnderlyingTeamMember { get; private set; }

    /// <summary>Roles like "Team member", "Knowledge contributor". Multiple if the person appears in both lists.</summary>
    public ObservableCollection<string> Tags { get; } = new();

    /// <summary>
    /// True when this person should be included in the next "Start group chat"
    /// click. Defaults <b>false</b> — the user explicitly checks the people
    /// they want to ping, rather than unchecking those they don't.
    /// </summary>
    [ObservableProperty]
    private bool _isSelectedForChat;

    /// <summary>True when at least one tag is "Team member".</summary>
    public bool IsTeamMember => Tags.Contains(TagTeamMember);

    /// <summary>True when GitHub link button should appear (still has a username, role left to attach).</summary>
    public bool CanLinkGitHubAccount => !string.IsNullOrWhiteSpace(GitHubUsername);

    public SidebarPersonViewModel(
        string displayName,
        string email,
        string avatarUrl,
        string atlassianAccountId = "",
        string gitHubUsername = "",
        TeamMember? underlyingTeamMember = null)
    {
        DisplayName = displayName;
        Email = email;
        AvatarUrl = avatarUrl;
        AtlassianAccountId = atlassianAccountId;
        GitHubUsername = gitHubUsername;
        UnderlyingTeamMember = underlyingTeamMember;
    }

    /// <summary>Adds a tag if not already present.</summary>
    public void AddTag(string tag)
    {
        if (!Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            Tags.Add(tag);
    }

    /// <summary>
    /// Adopts a TeamMember reference if this person didn't originate from one.
    /// Keeps existing TeamMember if already set (priority: Jira/GitHub origin).
    /// </summary>
    public void AdoptTeamMember(TeamMember teamMember)
    {
        UnderlyingTeamMember ??= teamMember;
    }

    /// <summary>
    /// True when this VM represents the same person as <paramref name="other"/>.
    /// Match priority: AccountId → GitHubUsername → Email → DisplayName (case-insensitive).
    /// </summary>
    public bool MatchesIdentity(string accountId = "", string gitHubUsername = "", string email = "", string displayName = "")
    {
        if (!string.IsNullOrEmpty(AtlassianAccountId)
            && !string.IsNullOrEmpty(accountId)
            && string.Equals(AtlassianAccountId, accountId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(GitHubUsername)
            && !string.IsNullOrEmpty(gitHubUsername)
            && string.Equals(GitHubUsername, gitHubUsername, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(Email)
            && !string.IsNullOrEmpty(email)
            && string.Equals(Email, email, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(DisplayName)
            && !string.IsNullOrEmpty(displayName)
            && string.Equals(DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
