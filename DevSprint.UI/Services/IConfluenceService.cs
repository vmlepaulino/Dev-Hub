using DevSprint.UI.Models;

namespace DevSprint.UI.Services;

/// <summary>
/// Read-only Confluence Cloud client. Phase 1 surface area: discover pages
/// that reference a Jira issue and surface their authors / last editors so the
/// UI can show "who knows about this" in the issue sidebar.
/// </summary>
public interface IConfluenceService
{
    /// <summary>
    /// Returns Confluence pages and blog posts related to a Jira issue. Two
    /// match strategies are combined into a single CQL query:
    /// <list type="bullet">
    ///   <item><b>Explicit references</b> — pages whose body or title mentions <paramref name="issueKey"/> verbatim.</item>
    ///   <item><b>Topical references</b> — pages whose <i>title</i> contains significant keywords pulled from
    ///     <paramref name="issueTitle"/> (stopwords and common backlog verbs are filtered out).</item>
    /// </list>
    /// Each returned <see cref="ConfluencePage"/> carries a <see cref="ConfluencePage.MatchReason"/>
    /// describing which strategy surfaced it.
    /// </summary>
    /// <param name="issueKey">Jira issue key, e.g. "ENG-123".</param>
    /// <param name="issueTitle">Issue summary used for keyword search. Optional — pass null to use key-only.</param>
    /// <param name="limit">Maximum pages to return. Default 25.</param>
    Task<IReadOnlyList<ConfluencePage>> GetPagesForIssueAsync(
        string issueKey,
        string? issueTitle = null,
        int limit = 25,
        CancellationToken cancellationToken = default);
}
