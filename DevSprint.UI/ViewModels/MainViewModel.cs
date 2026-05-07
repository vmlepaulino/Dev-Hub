using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevSprint.UI.Auth.GitHub;
using DevSprint.UI.Auth.Jira;
using DevSprint.UI.Models;
using DevSprint.UI.Onboarding;
using DevSprint.UI.Services;
using DevSprint.UI.Views;
using Microsoft.Extensions.Configuration;
using System.Windows;

namespace DevSprint.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IJiraService _jiraService;
    private readonly IGitHubService _gitHubService;
    private readonly IConfluenceService _confluenceService;
    private readonly IIdentityService _identityService;
    private readonly IGitHubAuthService _gitHubAuthService;
    private readonly IJiraAuthService _jiraAuthService;
    private readonly IOnboardingService _onboardingService;
    private readonly IConfiguration _configuration;

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
    private SprintInfo? _selectedSprint;

    public ObservableCollection<SprintInfo> AvailableSprints { get; } = [];

    partial void OnSelectedSprintChanged(SprintInfo? value)
    {
        if (value is not null)
            _ = LoadSprintDataAsync(value);
    }

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
        if (string.IsNullOrWhiteSpace(value))
            FilterText = string.Empty;
    }

    [ObservableProperty]
    private string _currentUserDisplayName = string.Empty;

    [ObservableProperty]
    private bool _isGitHubSignedIn;

    [ObservableProperty]
    private bool _isJiraSignedIn;

    [RelayCommand]
    private async Task SignOutGitHubAsync()
    {
        await _gitHubAuthService.SignOutAsync();
        IsGitHubSignedIn = false;
        ErrorMessage = "Signed out of GitHub. The next GitHub action will prompt you to sign in again.";
    }

    [RelayCommand]
    private async Task SignOutJiraAsync()
    {
        await _jiraAuthService.SignOutAsync();
        IsJiraSignedIn = false;
        ErrorMessage = "Signed out of Jira. The next Jira action will prompt you to sign in again.";
    }

    [RelayCommand]
    private void Reconfigure()
    {
        var snapshot = _onboardingService.Snapshot(_configuration);
        var vm = new OnboardingWizardViewModel(_onboardingService, snapshot);
        var dialog = new OnboardingWizardDialog(vm)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true) return;

        // Configuration is loaded once at startup; new values won't take effect
        // until the app restarts. Tell the user, then close the app for them.
        var result = MessageBox.Show(
            "Settings saved. DevSprint needs to restart to pick up the new values.\n\nClose the application now?",
            "Restart required",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.OK)
            Application.Current.Shutdown();
    }

    [ObservableProperty]
    private bool _isFirstRun;

    [ObservableProperty]
    private string _welcomeName = string.Empty;

    public ObservableCollection<TeamIdentity> TeamMembers { get; } = [];

    [ObservableProperty]
    private string _filterText = string.Empty;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilterText = string.Empty;
            return;
        }

        var key = SearchText.Trim();

        // Filter the current visible list by key (partial match)
        FilterText = key;

        // If exact match found in any list, also open the sidebar
        var match = BacklogIssues.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                 ?? SprintIssues.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                 ?? AssignedIssues.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                 ?? ContributingIssues.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            await SelectIssueCommand.ExecuteAsync(match);
            return;
        }

        // Fall back to Jira API
        try
        {
            var issue = await _jiraService.GetIssueByKeyAsync(key);
            if (issue is not null)
            {
                issue.IsCurrentSprint = _sprintKeys.Contains(issue.Key);
                await SelectIssueCommand.ExecuteAsync(issue);
            }
        }
        catch
        {
            // Silently ignore search errors
        }
    }

    public ObservableCollection<TeamMember> SidebarTeamMembers { get; } = [];
    public ObservableCollection<BranchInfo> SidebarBranches { get; } = [];
    public ObservableCollection<ConfluencePage> SidebarConfluencePages { get; } = [];

    [ObservableProperty]
    private string _sidebarConfluenceStatus = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LinkGitHubMemberCommand))]
    private TeamMember? _gitHubMemberToLink;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LinkGitHubMemberCommand))]
    private TeamIdentity? _selectedIdentityForGitHubLink;

    [ObservableProperty]
    private bool _isGitHubLinkOpen;

    public ObservableCollection<JiraIssue> BacklogIssues { get; } = [];
    public ObservableCollection<JiraIssue> SprintIssues { get; } = [];
    public ObservableCollection<JiraIssue> AssignedIssues { get; } = [];
    public ObservableCollection<JiraIssue> ContributingIssues { get; } = [];

    public ICollectionView BacklogView { get; }
    public ICollectionView SprintView { get; }
    public ICollectionView AssignedView { get; }
    public ICollectionView ContributingView { get; }

    public MainViewModel(
        IJiraService jiraService,
        IGitHubService gitHubService,
        IConfluenceService confluenceService,
        IIdentityService identityService,
        IGitHubAuthService gitHubAuthService,
        IJiraAuthService jiraAuthService,
        IOnboardingService onboardingService,
        IConfiguration configuration)
    {
        _jiraService = jiraService;
        _gitHubService = gitHubService;
        _confluenceService = confluenceService;
        _identityService = identityService;
        _gitHubAuthService = gitHubAuthService;
        _jiraAuthService = jiraAuthService;
        _onboardingService = onboardingService;
        _configuration = configuration;

        IsGitHubSignedIn = _gitHubAuthService.IsSignedIn;
        IsJiraSignedIn = _jiraAuthService.IsSignedIn;

        BacklogView = CollectionViewSource.GetDefaultView(BacklogIssues);
        SprintView = CollectionViewSource.GetDefaultView(SprintIssues);
        AssignedView = CollectionViewSource.GetDefaultView(AssignedIssues);
        ContributingView = CollectionViewSource.GetDefaultView(ContributingIssues);

        BacklogView.Filter = IssueMatchesFilter;
        SprintView.Filter = IssueMatchesFilter;
        AssignedView.Filter = IssueMatchesFilter;
        ContributingView.Filter = IssueMatchesFilter;
    }

    private bool IssueMatchesFilter(object obj)
    {
        if (string.IsNullOrWhiteSpace(FilterText)) return true;
        if (obj is not JiraIssue issue) return false;

        return issue.Key.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            || issue.Summary.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnFilterTextChanged(string value)
    {
        BacklogView.Refresh();
        SprintView.Refresh();
        AssignedView.Refresh();
        ContributingView.Refresh();
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
        TeamMembers.Clear();
        _sprintKeys = [];
        _contributingKeys = [];


        try
        {
            // Load identity store and check first run
            await _identityService.LoadAsync();
            var currentUser = _identityService.GetCurrentUser();
            if (currentUser is null)
            {
                var myself = await _jiraService.GetMyselfAsync();
                if (myself is not null)
                {
                    WelcomeName = myself.JiraDisplayName;
                    IsFirstRun = true;
                    _identityService.SetCurrentUser(myself);
                    await _identityService.SaveAsync();
                    currentUser = myself;
                }
            }
            CurrentUserDisplayName = currentUser?.DisplayName ?? string.Empty;

            // Discover team from board + load data in parallel
            var boardMembersTask = _jiraService.GetBoardMembersAsync();
            var sprintsTask = _jiraService.GetSprintsForQuarterAsync();
            var sprintKeysTask = _jiraService.GetCurrentSprintKeysAsync();
            var backlogTask = _jiraService.GetProductBacklogAsync(0, InitialPageSize);
            var assignedTask = _jiraService.GetMyIssuesAsync(0, InitialPageSize);
            var contributingTask = _jiraService.GetMyCommentedIssuesAsync(0, InitialPageSize);

            await Task.WhenAll(boardMembersTask, sprintsTask, sprintKeysTask, backlogTask, assignedTask, contributingTask);

            // Merge discovered team members
            _identityService.MergeFromJira(boardMembersTask.Result);
            await _identityService.SaveAsync();

            foreach (var member in _identityService.GetAll().OrderBy(m => m.DisplayName))
                TeamMembers.Add(member);

            _sprintKeys = sprintKeysTask.Result;

            // Populate sprint dropdown
            AvailableSprints.Clear();
            foreach (var s in sprintsTask.Result)
                AvailableSprints.Add(s);

            // Select the active sprint (or first)
            var activeSprint = AvailableSprints.FirstOrDefault(s => s.State.Equals("active", StringComparison.OrdinalIgnoreCase))
                            ?? AvailableSprints.FirstOrDefault();
            SelectedSprint = activeSprint;

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
        if (!_sprintHasMore || _isLoadingMoreSprint || SelectedSprint is null) return;
        _isLoadingMoreSprint = true;
        try
        {
            var result = await _jiraService.GetSprintIssuesAsync(SelectedSprint.Id, _sprintNextStartAt, ScrollPageSize);
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

    private async Task LoadSprintDataAsync(SprintInfo sprint)
    {
        SprintIssues.Clear();
        SprintName = sprint.Name;
        SprintDateRange = sprint.StartDate.HasValue && sprint.EndDate.HasValue
            ? $"{sprint.StartDate:dd/MM/yyyy} — {sprint.EndDate:dd/MM/yyyy}"
            : string.Empty;

        try
        {
            var result = await _jiraService.GetSprintIssuesAsync(sprint.Id, 0, InitialPageSize);
            foreach (var issue in result.Items)
            {
                issue.IsCurrentSprint = true;
                SprintIssues.Add(issue);
            }
            _sprintNextStartAt = result.NextStartAt;
            _sprintHasMore = result.HasMore;
            UpdateSprintStats();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
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
        SidebarConfluencePages.Clear();
        SidebarConfluenceStatus = string.Empty;
        IsGitHubLinkOpen = false;
        GitHubMemberToLink = null;
        SelectedIdentityForGitHubLink = null;
        OpenTeamsGroupCommand.NotifyCanExecuteChanged();

        try
        {
            // Add Jira assignee as team member
            if (!string.IsNullOrEmpty(issue.Assignee))
            {
                var assigneeIdentity = ResolveTeamIdentity(issue.Assignee);
                SidebarTeamMembers.Add(new TeamMember
                {
                    Name = assigneeIdentity?.DisplayName ?? issue.Assignee,
                    Email = assigneeIdentity?.Email ?? string.Empty,
                    Role = "Assignee"
                });
            }

            var sprintStart = SelectedSprint?.StartDate;
            var branchesTask = _gitHubService.GetBranchesForIssueAsync(issue.Key, sprintStart);
            var contributorsTask = _gitHubService.GetContributorsForIssueAsync(issue.Key, sprintStart);
            var confluencePagesTask = _confluenceService.GetPagesForIssueAsync(issue.Key, issue.Summary);

            await Task.WhenAll(branchesTask, contributorsTask, confluencePagesTask);

            foreach (var member in contributorsTask.Result)
            {
                if (!SidebarTeamMembers.Any(m => IsSameSidebarMember(m, member)))
                    SidebarTeamMembers.Add(member);
            }

            foreach (var branch in branchesTask.Result)
                SidebarBranches.Add(branch);

            foreach (var page in confluencePagesTask.Result)
                SidebarConfluencePages.Add(page);

            SidebarConfluenceStatus = SidebarConfluencePages.Count == 0
                ? "No Confluence pages mention this issue."
                : $"{SidebarConfluencePages.Count} page{(SidebarConfluencePages.Count == 1 ? "" : "s")} mention this issue.";
        }
        catch
        {
            // Silently handle — sidebar shows what we have
        }
        finally
        {
            IsSidebarLoading = false;
            OpenTeamsGroupCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void CloseSidebar()
    {
        IsSidebarOpen = false;
        SelectedIssue = null;
        IsGitHubLinkOpen = false;
        GitHubMemberToLink = null;
        SelectedIdentityForGitHubLink = null;
        OpenTeamsGroupCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task ConfirmWelcomeAsync()
    {
        if (!string.IsNullOrWhiteSpace(WelcomeName))
        {
            var current = _identityService.GetCurrentUser();
            if (current is not null)
            {
                current.DisplayName = WelcomeName.Trim();
                await _identityService.SaveAsync();
                CurrentUserDisplayName = current.DisplayName;
            }
        }
        IsFirstRun = false;
    }

    [RelayCommand]
    private void StartGitHubLink(TeamMember? member)
    {
        if (member is null || string.IsNullOrWhiteSpace(member.GitHubUsername)) return;

        GitHubMemberToLink = member;
        SelectedIdentityForGitHubLink = ResolveTeamIdentity(member);
        IsGitHubLinkOpen = true;
    }

    [RelayCommand]
    private void CancelGitHubLink()
    {
        IsGitHubLinkOpen = false;
        GitHubMemberToLink = null;
        SelectedIdentityForGitHubLink = null;
    }

    private bool CanLinkGitHubMember() =>
        GitHubMemberToLink is not null
        && SelectedIdentityForGitHubLink is not null
        && !string.IsNullOrWhiteSpace(GitHubMemberToLink.GitHubUsername);

    [RelayCommand(CanExecute = nameof(CanLinkGitHubMember))]
    private async Task LinkGitHubMemberAsync()
    {
        if (GitHubMemberToLink is null || SelectedIdentityForGitHubLink is null) return;

        var sourceMember = GitHubMemberToLink;
        var linkedIdentity = SelectedIdentityForGitHubLink;
        _identityService.LinkGitHubUsername(linkedIdentity, sourceMember.GitHubUsername);
        await _identityService.SaveAsync();

        var index = SidebarTeamMembers.IndexOf(sourceMember);
        if (index >= 0)
        {
            SidebarTeamMembers[index] = new TeamMember
            {
                Name = linkedIdentity.DisplayName,
                Email = linkedIdentity.Email,
                GitHubUsername = sourceMember.GitHubUsername,
                AvatarUrl = !string.IsNullOrWhiteSpace(linkedIdentity.AvatarUrl)
                    ? linkedIdentity.AvatarUrl
                    : sourceMember.AvatarUrl,
                Role = sourceMember.Role
            };
        }

        ErrorMessage = string.Empty;
        CancelGitHubLink();
        OpenTeamsGroupCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenTeams(object? memberObj)
    {
        var participant = ResolveTeamsParticipant(memberObj);
        if (string.IsNullOrWhiteSpace(participant)) return;

        OpenTeamsChat([participant]);
    }

    private bool CanOpenTeamsGroup() =>
        IsSidebarOpen
        && SelectedIssue is not null
        && SidebarTeamMembers.Count > 0;

    [RelayCommand(CanExecute = nameof(CanOpenTeamsGroup))]
    private void OpenTeamsGroup()
    {
        var participants = SidebarTeamMembers
            .Select(ResolveTeamsParticipant)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Where(p => !IsCurrentUserParticipant(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (participants.Count == 0)
        {
            ErrorMessage = "No Teams addresses were found for the people on this work item.";
            return;
        }

        var issue = SelectedIssue;
        ErrorMessage = string.Empty;
        OpenTeamsChat(participants, CreateTeamsTopic(issue), CreateTeamsMessage(issue));
    }

    private string ResolveTeamsParticipant(object? memberObj)
    {
        if (memberObj is null) return string.Empty;
        if (memberObj is string value) return NormalizeTeamsAddress(value);

        if (memberObj is TeamMember member && !string.IsNullOrWhiteSpace(member.Email))
            return NormalizeTeamsAddress(member.Email);

        var identity = memberObj switch
        {
            TeamIdentity teamIdentity => teamIdentity,
            TeamMember teamMember => ResolveTeamIdentity(teamMember),
            _ => null
        };

        return identity is not null
            ? NormalizeTeamsAddress(identity.Email)
            : string.Empty;
    }

    private static string NormalizeTeamsAddress(string value)
    {
        var trimmed = value.Trim();
        return IsTeamsAddress(trimmed) ? trimmed : string.Empty;
    }

    private static bool IsTeamsAddress(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains('@')
        && !value.Any(char.IsWhiteSpace)
        && !value.Contains(',')
        && !value.Contains(';');

    private TeamIdentity? ResolveTeamIdentity(TeamMember member) =>
        ResolveTeamIdentity(member.GitHubUsername, allowGitHubUsername: true)
        ?? (!string.IsNullOrWhiteSpace(member.Email)
            ? ResolveTeamIdentity(member.Email) ?? ResolveTeamIdentity(member.Name)
            : ResolveTeamIdentity(member.Name));

    private TeamIdentity? ResolveTeamIdentity(string nameOrEmail, bool allowGitHubUsername = false)
    {
        if (string.IsNullOrWhiteSpace(nameOrEmail)) return null;

        if (allowGitHubUsername)
        {
            var identity = _identityService.GetByGitHubUsername(nameOrEmail);
            if (identity is not null) return identity;
        }

        return TeamMembers.FirstOrDefault(t =>
            string.Equals(t.Email, nameOrEmail, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.DisplayName, nameOrEmail, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.JiraDisplayName, nameOrEmail, StringComparison.OrdinalIgnoreCase)
            || (allowGitHubUsername && string.Equals(t.GitHubUsername, nameOrEmail, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsSameSidebarMember(TeamMember left, TeamMember right) =>
        MatchesKnownIdentityValue(left.Email, right.Email)
        || MatchesKnownIdentityValue(left.GitHubUsername, right.GitHubUsername)
        || string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

    private bool IsCurrentUserParticipant(string participant)
    {
        var currentUser = _identityService.GetCurrentUser();
        if (currentUser is null) return false;

        return MatchesKnownIdentityValue(participant, currentUser.Email)
            || MatchesKnownIdentityValue(participant, currentUser.DisplayName)
            || MatchesKnownIdentityValue(participant, currentUser.JiraDisplayName);
    }

    private static bool MatchesKnownIdentityValue(string participant, string value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(participant, value, StringComparison.OrdinalIgnoreCase);

    private static string CreateTeamsTopic(JiraIssue? issue)
    {
        if (issue is null || string.IsNullOrWhiteSpace(issue.Key))
            return "Work item collaboration";

        var topic = string.IsNullOrWhiteSpace(issue.Summary)
            ? issue.Key
            : $"{issue.Key} - {issue.Summary}";

        return topic.Length <= 90 ? topic : topic[..90];
    }

    private static string CreateTeamsMessage(JiraIssue? issue)
    {
        if (issue is null || string.IsNullOrWhiteSpace(issue.Key))
            return "Hi team, starting a collaboration chat for this work item.";

        return $"Hi team, starting a collaboration chat for {issue.Key}: {issue.Summary}{Environment.NewLine}{issue.BrowseUrl}";
    }

    private static void OpenTeamsChat(IEnumerable<string> participants, string? topicName = null, string? message = null)
    {
        var users = participants
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (users.Count == 0) return;

        var query = new List<string>
        {
            $"users={string.Join(",", users.Select(WebUtility.UrlEncode))}"
        };

        if (!string.IsNullOrWhiteSpace(topicName))
            query.Add($"topicName={WebUtility.UrlEncode(topicName)}");

        if (!string.IsNullOrWhiteSpace(message))
            query.Add($"message={WebUtility.UrlEncode(message)}");

        var target = $"https://teams.microsoft.com/l/chat/0/0?{string.Join("&", query)}";
        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch
        {
            // best-effort; ignore errors
        }
    }
}
