namespace DevSprint.UI.Onboarding;

/// <summary>
/// A logical group of configuration fields shown as one wizard step. Today
/// there are two: "Backlog Management" (Jira) and "Code Repository" (GitHub).
/// </summary>
public sealed class OnboardingFieldGroup
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? RegistrationUrl { get; init; }
    public required IReadOnlyList<OnboardingFieldDefinition> Fields { get; init; }

    /// <summary>
    /// Optional reminder shown at the bottom of the step (e.g. "Set ClientSecret
    /// separately via dotnet user-secrets"). Rendered as info, not error.
    /// </summary>
    public string? PostStepNote { get; init; }
}

/// <summary>
/// Static catalogue of every field the onboarding wizard knows about. Keeping
/// the field list in one place makes it easy to extend without editing the
/// service or the wizard view-model.
/// </summary>
public static class OnboardingCatalog
{
    public const string JiraClientSecretKey = "Jira:OAuth:ClientSecret";
    public const string DefaultCallbackPort = "7890";

    public static OnboardingFieldGroup BacklogManagement { get; } = new()
    {
        Title = "Backlog Management",
        Description = "Connect to your Atlassian site so DevSprint can show your backlog, sprints, and assigned issues.",
        RegistrationUrl = "https://developer.atlassian.com/console/myapps/",
        Fields =
        [
            new OnboardingFieldDefinition
            {
                Key = "Jira:BaseUrl",
                Label = "Atlassian site URL",
                Placeholder = "https://your-site.atlassian.net/",
                HelpText = "The full URL of your Atlassian Cloud site, including the trailing slash."
            },
            new OnboardingFieldDefinition
            {
                Key = "Jira:Email",
                Label = "Account email",
                Placeholder = "you@example.com",
                HelpText = "The email address you sign in to Atlassian with."
            },
            new OnboardingFieldDefinition
            {
                Key = "Jira:BoardId",
                Label = "Board ID",
                Placeholder = "e.g. 42",
                HelpText = "Numeric ID from your Jira board's URL: …/jira/software/projects/XXX/boards/<this>.",
                IsNumeric = true
            },
            new OnboardingFieldDefinition
            {
                Key = "Jira:ProjectKey",
                Label = "Project key",
                Placeholder = "e.g. ENG",
                HelpText = "The letter prefix of your issue keys (the part before the dash in ENG-123)."
            },
            new OnboardingFieldDefinition
            {
                Key = "Jira:OAuth:ClientId",
                Label = "OAuth Client ID",
                Placeholder = "Client ID from developer.atlassian.com",
                HelpText = "From your OAuth 2.0 (3LO) integration → Settings → Authentication details.",
                HelpUrl = "https://developer.atlassian.com/console/myapps/"
            },
            new OnboardingFieldDefinition
            {
                Key = JiraClientSecretKey,
                Label = "OAuth Client Secret",
                Placeholder = "Client Secret from developer.atlassian.com",
                HelpText = "Same OAuth app's Settings page. Stored encrypted on disk under your Windows account.",
                IsSecret = true,
                HelpUrl = "https://developer.atlassian.com/console/myapps/"
            },
            new OnboardingFieldDefinition
            {
                Key = "Jira:OAuth:CallbackPort",
                Label = "OAuth callback port",
                Placeholder = DefaultCallbackPort,
                DefaultValue = DefaultCallbackPort,
                HelpText = "Local loopback port. Must match the Callback URL registered on the OAuth app: http://127.0.0.1:<port>/callback.",
                IsNumeric = true
            }
        ]
    };

    public static OnboardingFieldGroup CodeRepository { get; } = new()
    {
        Title = "Code Repository",
        Description = "Connect to GitHub so DevSprint can surface PRs, branches, and contributors related to each work item.",
        RegistrationUrl = "https://github.com/settings/developers",
        Fields =
        [
            new OnboardingFieldDefinition
            {
                Key = "GitHub:Username",
                Label = "GitHub username",
                Placeholder = "your-github-handle",
                HelpText = "Used to filter \"my pull requests\" and to surface your activity on issues."
            },
            new OnboardingFieldDefinition
            {
                Key = "GitHub:Organization",
                Label = "GitHub organization",
                Placeholder = "your-org",
                HelpText = "The organization (or user) that owns the repositories DevSprint should track."
            },
            new OnboardingFieldDefinition
            {
                Key = "GitHub:Repositories",
                Label = "Repositories",
                Placeholder = "repo-one, repo-two",
                HelpText = "Comma-separated list. These are the repos scanned for PRs and branches per Jira issue.",
                IsArray = true
            },
            new OnboardingFieldDefinition
            {
                Key = "GitHub:OAuth:ClientId",
                Label = "OAuth Client ID",
                Placeholder = "Client ID from github.com OAuth App",
                HelpText = "From github.com → Settings → Developer settings → OAuth Apps → your app. Make sure Device Flow is enabled.",
                HelpUrl = "https://github.com/settings/developers"
            }
        ]
    };

    public static IReadOnlyList<OnboardingFieldGroup> AllGroups { get; } =
    [
        BacklogManagement,
        CodeRepository
    ];

    public static IEnumerable<OnboardingFieldDefinition> AllFields =>
        AllGroups.SelectMany(g => g.Fields);
}
