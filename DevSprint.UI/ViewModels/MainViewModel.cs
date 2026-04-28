using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevSprint.UI.Models;
using DevSprint.UI.Services;

namespace DevSprint.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IJiraService _jiraService;

    private int _backlogNextStartAt;
    private int _myIssuesNextStartAt;
    private int _commentedNextStartAt;
    private HashSet<string> _sprintKeys = [];
    private HashSet<string> _myIssueKeys = [];

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today.AddDays(-14);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasMoreBacklog;

    [ObservableProperty]
    private bool _hasMoreMyIssues;

    [ObservableProperty]
    private string _backlogStatus = string.Empty;

    [ObservableProperty]
    private string _myIssuesStatus = string.Empty;

    public ObservableCollection<JiraIssue> BacklogIssues { get; } = [];
    public ObservableCollection<JiraIssue> MyIssues { get; } = [];

    public MainViewModel(IJiraService jiraService)
    {
        _jiraService = jiraService;
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        BacklogIssues.Clear();
        MyIssues.Clear();
        _sprintKeys = [];
        _myIssueKeys = [];
        _backlogNextStartAt = 0;
        _myIssuesNextStartAt = 0;
        _commentedNextStartAt = 0;

        try
        {
            var backlogTask = _jiraService.GetBacklogAsync(FromDate, ToDate, 0);
            var assignedTask = _jiraService.GetMyIssuesAsync(FromDate, ToDate, 0);
            var commentedTask = _jiraService.GetMyCommentedIssuesAsync(FromDate, ToDate, 0);

            await Task.WhenAll(backlogTask, assignedTask, commentedTask);

            var backlogResult = backlogTask.Result;
            var assignedResult = assignedTask.Result;
            var commentedResult = commentedTask.Result;

            // Track sprint keys
            foreach (var issue in backlogResult.Items)
            {
                _sprintKeys.Add(issue.Key);
                issue.IsCurrentSprint = true;
                BacklogIssues.Add(issue);
            }

            _backlogNextStartAt = backlogResult.NextStartAt;
            HasMoreBacklog = backlogResult.HasMore;
            BacklogStatus = $"Showing {BacklogIssues.Count} of {backlogResult.Total}";

            // Merge assigned + commented
            AddMyIssues(assignedResult.Items);
            AddMyIssues(commentedResult.Items);

            _myIssuesNextStartAt = assignedResult.NextStartAt;
            _commentedNextStartAt = commentedResult.NextStartAt;
            HasMoreMyIssues = assignedResult.HasMore || commentedResult.HasMore;
            MyIssuesStatus = $"Showing {MyIssues.Count} of {assignedResult.Total + commentedResult.Total}";
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
    private async Task LoadMoreBacklogAsync()
    {
        if (!HasMoreBacklog) return;
        IsLoading = true;

        try
        {
            var result = await _jiraService.GetBacklogAsync(FromDate, ToDate, _backlogNextStartAt);

            foreach (var issue in result.Items)
            {
                _sprintKeys.Add(issue.Key);
                issue.IsCurrentSprint = true;
                BacklogIssues.Add(issue);
            }

            _backlogNextStartAt = result.NextStartAt;
            HasMoreBacklog = result.HasMore;
            BacklogStatus = $"Showing {BacklogIssues.Count} of {result.Total}";
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
    private async Task LoadMoreMyIssuesAsync()
    {
        if (!HasMoreMyIssues) return;
        IsLoading = true;

        try
        {
            var assignedResult = _myIssuesNextStartAt >= 0
                ? await _jiraService.GetMyIssuesAsync(FromDate, ToDate, _myIssuesNextStartAt)
                : new PagedResult<JiraIssue>();

            var commentedResult = _commentedNextStartAt >= 0
                ? await _jiraService.GetMyCommentedIssuesAsync(FromDate, ToDate, _commentedNextStartAt)
                : new PagedResult<JiraIssue>();

            AddMyIssues(assignedResult.Items);
            AddMyIssues(commentedResult.Items);

            _myIssuesNextStartAt = assignedResult.HasMore ? assignedResult.NextStartAt : -1;
            _commentedNextStartAt = commentedResult.HasMore ? commentedResult.NextStartAt : -1;
            HasMoreMyIssues = assignedResult.HasMore || commentedResult.HasMore;

            var totalEstimate = assignedResult.Total + commentedResult.Total;
            MyIssuesStatus = $"Showing {MyIssues.Count} of ~{totalEstimate}";
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

    private void AddMyIssues(IReadOnlyList<JiraIssue> issues)
    {
        foreach (var issue in issues)
        {
            if (!_myIssueKeys.Add(issue.Key)) continue;
            issue.IsCurrentSprint = _sprintKeys.Contains(issue.Key);
            MyIssues.Add(issue);
        }
    }
}
