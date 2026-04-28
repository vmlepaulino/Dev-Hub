using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevSprint.UI.Models;
using DevSprint.UI.Services;

namespace DevSprint.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IJiraService _jiraService;

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today.AddDays(-14);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

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

        try
        {
            var backlogTask = _jiraService.GetBacklogAsync(FromDate, ToDate);
            var assignedTask = _jiraService.GetMyIssuesAsync(FromDate, ToDate);
            var commentedTask = _jiraService.GetMyCommentedIssuesAsync(FromDate, ToDate);

            await Task.WhenAll(backlogTask, assignedTask, commentedTask);

            foreach (var issue in backlogTask.Result)
            {
                BacklogIssues.Add(issue);
            }

            var myIssues = assignedTask.Result
                .Concat(commentedTask.Result)
                .DistinctBy(i => i.Key)
                .OrderByDescending(i => i.Updated);

            foreach (var issue in myIssues)
            {
                MyIssues.Add(issue);
            }
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
}
