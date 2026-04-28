using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevSprint.UI.Models;
using DevSprint.UI.Services;

namespace DevSprint.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IJiraService _jiraService;

    private const int InitialPageSize = 100;
    private const int ScrollPageSize = 10;

    private int _backlogNextStartAt;
    private int _myIssuesNextStartAt;
    private int _commentedNextStartAt;
    private bool _backlogHasMore;
    private bool _myIssuesHasMore;
    private bool _commentedHasMore;
    private bool _isLoadingMoreBacklog;
    private bool _isLoadingMoreMyIssues;
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

        try
        {
            var backlogTask = _jiraService.GetBacklogAsync(FromDate, ToDate, 0, InitialPageSize);
            var assignedTask = _jiraService.GetMyIssuesAsync(FromDate, ToDate, 0, InitialPageSize);
            var commentedTask = _jiraService.GetMyCommentedIssuesAsync(FromDate, ToDate, 0, InitialPageSize);

            await Task.WhenAll(backlogTask, assignedTask, commentedTask);

            var backlogResult = backlogTask.Result;
            var assignedResult = assignedTask.Result;
            var commentedResult = commentedTask.Result;

            foreach (var issue in backlogResult.Items)
            {
                _sprintKeys.Add(issue.Key);
                issue.IsCurrentSprint = true;
                BacklogIssues.Add(issue);
            }

            _backlogNextStartAt = backlogResult.NextStartAt;
            _backlogHasMore = backlogResult.HasMore;
            BacklogStatus = $"Showing {BacklogIssues.Count} of {backlogResult.Total}";

            AddMyIssues(assignedResult.Items);
            AddMyIssues(commentedResult.Items);

            _myIssuesNextStartAt = assignedResult.NextStartAt;
            _commentedNextStartAt = commentedResult.NextStartAt;
            _myIssuesHasMore = assignedResult.HasMore;
            _commentedHasMore = commentedResult.HasMore;
            UpdateMyIssuesStatus(assignedResult.Total, commentedResult.Total);
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
            var result = await _jiraService.GetBacklogAsync(FromDate, ToDate, _backlogNextStartAt, ScrollPageSize);

            foreach (var issue in result.Items)
            {
                _sprintKeys.Add(issue.Key);
                issue.IsCurrentSprint = true;
                BacklogIssues.Add(issue);
            }

            _backlogNextStartAt = result.NextStartAt;
            _backlogHasMore = result.HasMore;
            BacklogStatus = $"Showing {BacklogIssues.Count} of {result.Total}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _isLoadingMoreBacklog = false;
        }
    }

    [RelayCommand]
    private async Task ScrollMyIssuesAsync()
    {
        if ((!_myIssuesHasMore && !_commentedHasMore) || _isLoadingMoreMyIssues) return;
        _isLoadingMoreMyIssues = true;

        try
        {
            if (_myIssuesHasMore)
            {
                var result = await _jiraService.GetMyIssuesAsync(FromDate, ToDate, _myIssuesNextStartAt, ScrollPageSize);
                AddMyIssues(result.Items);
                _myIssuesNextStartAt = result.NextStartAt;
                _myIssuesHasMore = result.HasMore;
            }

            if (_commentedHasMore)
            {
                var result = await _jiraService.GetMyCommentedIssuesAsync(FromDate, ToDate, _commentedNextStartAt, ScrollPageSize);
                AddMyIssues(result.Items);
                _commentedNextStartAt = result.NextStartAt;
                _commentedHasMore = result.HasMore;
            }

            MyIssuesStatus = $"Showing {MyIssues.Count}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _isLoadingMoreMyIssues = false;
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

    private void UpdateMyIssuesStatus(int assignedTotal, int commentedTotal)
    {
        MyIssuesStatus = $"Showing {MyIssues.Count} of ~{assignedTotal + commentedTotal}";
    }
}
