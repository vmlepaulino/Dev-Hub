using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevSprint.UI.Models;
using DevSprint.UI.Services;

namespace DevSprint.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IJiraService _jiraService;
    private readonly IGitHubService _gitHubService;

    private const int InitialPageSize = 100;
    private const int ScrollPageSize = 10;

    private int _backlogNextStartAt;
    private bool _backlogHasMore;
    private bool _isLoadingMoreBacklog;

    private int _sprintNextStartAt;
    private bool _sprintHasMore;
    private bool _isLoadingMoreSprint;

    private int _assignedNextStartAt;
    private bool _assignedHasMore;
    private bool _isLoadingMoreAssigned;

    private int _contributingNextStartAt;
    private bool _contributingHasMore;
    private bool _isLoadingMoreContributing;
    private HashSet<string> _contributingKeys = [];

    private HashSet<string> _sprintKeys = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _backlogStatus = string.Empty;

    [ObservableProperty]
    private int _backlogTotalCount;

    [ObservableProperty]
    private int _backlogRefinedCount;

    [ObservableProperty]
    private int _backlogInAnalysisCount;

    [ObservableProperty]
    private string _sprintStatus = string.Empty;

    [ObservableProperty]
    private string _sprintName = string.Empty;

    [ObservableProperty]
    private string _sprintDateRange = string.Empty;

    [ObservableProperty]
    private double _sprintTotalStoryPoints;

    [ObservableProperty]
    private string _sprintStateSummary = string.Empty;

    [ObservableProperty]
    private string _assignedStatus = string.Empty;

    [ObservableProperty]
    private string _contributingStatus = string.Empty;

    [ObservableProperty]
    private JiraIssue? _selectedIssue;

    [ObservableProperty]
    private bool _isSidebarOpen;

    [ObservableProperty]
    private bool _isSidebarLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var key = value.Trim().ToUpperInvariant();
        var match = BacklogIssues.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                 ?? SprintIssues.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                 ?? AssignedIssues.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                 ?? ContributingIssues.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            SelectIssueCommand.Execute(match);
    }

    public ObservableCollection<TeamMember> SidebarTeamMembers { get; } = [];
    public ObservableCollection<BranchInfo> SidebarBranches { get; } = [];

    public ObservableCollection<JiraIssue> BacklogIssues { get; } = [];
    public ObservableCollection<JiraIssue> SprintIssues { get; } = [];
    public ObservableCollection<JiraIssue> AssignedIssues { get; } = [];
    public ObservableCollection<JiraIssue> ContributingIssues { get; } = [];

    public MainViewModel(IJiraService jiraService, IGitHubService gitHubService)
    {
        _jiraService = jiraService;
        _gitHubService = gitHubService;
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        BacklogIssues.Clear();
        SprintIssues.Clear();
        AssignedIssues.Clear();
        ContributingIssues.Clear();
        _sprintKeys = [];
        _contributingKeys = [];

        try
        {
            var sprintInfoTask = _jiraService.GetActiveSprintInfoAsync();
            var sprintKeysTask = _jiraService.GetCurrentSprintKeysAsync();
            var backlogTask = _jiraService.GetProductBacklogAsync(0, InitialPageSize);
            var sprintTask = _jiraService.GetCurrentSprintIssuesAsync(0, InitialPageSize);
            var assignedTask = _jiraService.GetMyIssuesAsync(0, InitialPageSize);
            var contributingTask = _jiraService.GetMyCommentedIssuesAsync(0, InitialPageSize);

            await Task.WhenAll(sprintInfoTask, sprintKeysTask, backlogTask, sprintTask, assignedTask, contributingTask);

            _sprintKeys = sprintKeysTask.Result;

            var sprintInfo = sprintInfoTask.Result;
            if (sprintInfo is not null)
            {
                SprintName = sprintInfo.Name;
                SprintDateRange = $"{sprintInfo.StartDate:dd/MM/yyyy} — {sprintInfo.EndDate:dd/MM/yyyy}";
            }

            var backlogResult = backlogTask.Result;
            foreach (var issue in backlogResult.Items)
            {
                issue.IsCurrentSprint = false;
                BacklogIssues.Add(issue);
            }
            _backlogNextStartAt = backlogResult.NextStartAt;
            _backlogHasMore = backlogResult.HasMore;
            BacklogTotalCount = backlogResult.Total;
            UpdateBacklogStats();

            var sprintResult = sprintTask.Result;
            foreach (var issue in sprintResult.Items)
            {
                issue.IsCurrentSprint = true;
                SprintIssues.Add(issue);
            }
            _sprintNextStartAt = sprintResult.NextStartAt;
            _sprintHasMore = sprintResult.HasMore;
            UpdateSprintStats();

            var assignedResult = assignedTask.Result;
            foreach (var issue in assignedResult.Items)
            {
                issue.IsCurrentSprint = _sprintKeys.Contains(issue.Key);
                AssignedIssues.Add(issue);
            }
            _assignedNextStartAt = assignedResult.NextStartAt;
            _assignedHasMore = assignedResult.HasMore;
            AssignedStatus = $"Showing {AssignedIssues.Count} of {assignedResult.Total}";

            var contributingResult = contributingTask.Result;
            foreach (var issue in contributingResult.Items)
            {
                if (_contributingKeys.Add(issue.Key))
                {
                    issue.IsCurrentSprint = _sprintKeys.Contains(issue.Key);
                    ContributingIssues.Add(issue);
                }
            }
            _contributingNextStartAt = contributingResult.NextStartAt;
            _contributingHasMore = contributingResult.HasMore;
            ContributingStatus = $"Showing {ContributingIssues.Count} of {contributingResult.Total}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ScrollBacklogAsync()
    {
        if (!_backlogHasMore || _isLoadingMoreBacklog) return;
        _isLoadingMoreBacklog = true;
        try
        {
            var result = await _jiraService.GetProductBacklogAsync(_backlogNextStartAt, ScrollPageSize);
            foreach (var issue in result.Items) { issue.IsCurrentSprint = false; BacklogIssues.Add(issue); }
            _backlogNextStartAt = result.NextStartAt;
            _backlogHasMore = result.HasMore;
            UpdateBacklogStats();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { _isLoadingMoreBacklog = false; }
    }

    [RelayCommand]
    private async Task ScrollSprintAsync()
    {
        if (!_sprintHasMore || _isLoadingMoreSprint) return;
        _isLoadingMoreSprint = true;
        try
        {
            var result = await _jiraService.GetCurrentSprintIssuesAsync(_sprintNextStartAt, ScrollPageSize);
            foreach (var issue in result.Items) { issue.IsCurrentSprint = true; SprintIssues.Add(issue); }
            _sprintNextStartAt = result.NextStartAt;
            _sprintHasMore = result.HasMore;
            UpdateSprintStats();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { _isLoadingMoreSprint = false; }
    }

    [RelayCommand]
    private async Task ScrollAssignedAsync()
    {
        if (!_assignedHasMore || _isLoadingMoreAssigned) return;
        _isLoadingMoreAssigned = true;
        try
        {
            var result = await _jiraService.GetMyIssuesAsync(_assignedNextStartAt, ScrollPageSize);
            foreach (var issue in result.Items) { issue.IsCurrentSprint = _sprintKeys.Contains(issue.Key); AssignedIssues.Add(issue); }
            _assignedNextStartAt = result.NextStartAt;
            _assignedHasMore = result.HasMore;
            AssignedStatus = $"Showing {AssignedIssues.Count} of {result.Total}";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { _isLoadingMoreAssigned = false; }
    }

    [RelayCommand]
    private async Task ScrollContributingAsync()
    {
        if (!_contributingHasMore || _isLoadingMoreContributing) return;
        _isLoadingMoreContributing = true;
        try
        {
            var result = await _jiraService.GetMyCommentedIssuesAsync(_contributingNextStartAt, ScrollPageSize);
            foreach (var issue in result.Items)
            {
                if (_contributingKeys.Add(issue.Key)) { issue.IsCurrentSprint = _sprintKeys.Contains(issue.Key); ContributingIssues.Add(issue); }
            }
            _contributingNextStartAt = result.NextStartAt;
            _contributingHasMore = result.HasMore;
            ContributingStatus = $"Showing {ContributingIssues.Count} of {result.Total}";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { _isLoadingMoreContributing = false; }
    }

    private void UpdateBacklogStats()
    {
        BacklogRefinedCount = BacklogIssues.Count(i =>
            i.Status.Contains("refin", StringComparison.OrdinalIgnoreCase) ||
            i.Status.Contains("ready", StringComparison.OrdinalIgnoreCase));
        BacklogInAnalysisCount = BacklogIssues.Count(i =>
            i.Status.Contains("analy", StringComparison.OrdinalIgnoreCase) ||
            i.Status.Contains("review", StringComparison.OrdinalIgnoreCase));
        BacklogStatus = $"Showing {BacklogIssues.Count} of {BacklogTotalCount}";
    }

    private void UpdateSprintStats()
    {
        SprintTotalStoryPoints = SprintIssues.Sum(i => i.StoryPoints);
        var groups = SprintIssues.GroupBy(i => i.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}");
        SprintStateSummary = string.Join("  ·  ", groups);
        SprintStatus = $"Showing {SprintIssues.Count}  ·  {SprintTotalStoryPoints} SP";
    }

    [RelayCommand]
    private async Task SelectIssueAsync(JiraIssue? issue)
    {
        if (issue is null) return;
        SelectedIssue = issue;
        IsSidebarOpen = true;
        IsSidebarLoading = true;
        SidebarTeamMembers.Clear();
        SidebarBranches.Clear();

        try
        {
            // Add Jira assignee as team member
            if (!string.IsNullOrEmpty(issue.Assignee))
            {
                SidebarTeamMembers.Add(new TeamMember
                {
                    Name = issue.Assignee,
                    Role = "Assignee"
                });
            }

            var branchesTask = _gitHubService.GetBranchesForIssueAsync(issue.Key);
            var contributorsTask = _gitHubService.GetContributorsForIssueAsync(issue.Key);

            await Task.WhenAll(branchesTask, contributorsTask);

            foreach (var member in contributorsTask.Result)
            {
                if (!SidebarTeamMembers.Any(m => m.Name.Equals(member.Name, StringComparison.OrdinalIgnoreCase)))
                    SidebarTeamMembers.Add(member);
            }

            foreach (var branch in branchesTask.Result)
                SidebarBranches.Add(branch);
        }
        catch
        {
            // Silently handle — sidebar shows what we have
        }
        finally
        {
            IsSidebarLoading = false;
        }
    }

    [RelayCommand]
    private void CloseSidebar()
    {
        IsSidebarOpen = false;
        SelectedIssue = null;
    }
}
