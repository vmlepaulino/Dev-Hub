namespace DevSprint.UI.Onboarding;

/// <summary>
/// Describes a single configuration value the wizard collects from the user.
/// Mapped 1-to-1 to a configuration key; Visualised as one form row in
/// <see cref="Views.OnboardingWizardDialog"/>.
/// </summary>
public sealed class OnboardingFieldDefinition
{
    /// <summary>Configuration key with colons (e.g. <c>Jira:OAuth:ClientId</c>).</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable label shown above the input.</summary>
    public required string Label { get; init; }

    /// <summary>Greyed example shown inside an empty input.</summary>
    public string Placeholder { get; init; } = string.Empty;

    /// <summary>Short help text shown below the input.</summary>
    public string HelpText { get; init; } = string.Empty;

    /// <summary>Pre-fill value when no current value exists.</summary>
    public string DefaultValue { get; init; } = string.Empty;

    /// <summary>True when the field must be non-empty before the wizard can complete.</summary>
    public bool IsRequired { get; init; } = true;

    /// <summary>
    /// True when the value should be treated as an array (comma-separated input,
    /// stored as a JSON array). Currently used for <c>GitHub:Repositories</c>.
    /// </summary>
    public bool IsArray { get; init; }

    /// <summary>True when the field is a port number / integer; UI may use a numeric input.</summary>
    public bool IsNumeric { get; init; }

    /// <summary>
    /// True when the value is a secret (Client Secret, etc.). UI renders a
    /// PasswordBox instead of a TextBox; storage is identical (DPAPI-encrypted).
    /// </summary>
    public bool IsSecret { get; init; }

    /// <summary>Optional URL to open if the user clicks a "where do I find this?" link.</summary>
    public string? HelpUrl { get; init; }
}
