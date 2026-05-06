using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevSprint.UI.Onboarding;

namespace DevSprint.UI.ViewModels;

/// <summary>
/// One row of the onboarding wizard form: label + input + validation message
/// + (optional) "where do I find this?" link. Wraps a single
/// <see cref="OnboardingFieldDefinition"/> together with its current value.
/// </summary>
public sealed partial class OnboardingFieldRowViewModel : ObservableObject
{
    public OnboardingFieldDefinition Field { get; }

    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private string _validationMessage = string.Empty;

    public bool HasHelpUrl => !string.IsNullOrEmpty(Field.HelpUrl);

    public OnboardingFieldRowViewModel(OnboardingFieldDefinition field, string initialValue)
    {
        Field = field;
        _value = string.IsNullOrEmpty(initialValue) ? field.DefaultValue : initialValue;
    }

    /// <summary>Validates the current value against the field's rules. Updates <see cref="ValidationMessage"/>.</summary>
    public bool Validate()
    {
        var trimmed = (Value ?? string.Empty).Trim();

        if (Field.IsRequired && string.IsNullOrEmpty(trimmed))
        {
            ValidationMessage = $"{Field.Label} is required.";
            return false;
        }

        if (Field.IsNumeric && !string.IsNullOrEmpty(trimmed) && !int.TryParse(trimmed, out _))
        {
            ValidationMessage = $"{Field.Label} must be a number.";
            return false;
        }

        if (Field.Key == "Jira:BaseUrl" && !string.IsNullOrEmpty(trimmed))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ValidationMessage = "Must be a full http(s) URL, e.g. https://your-site.atlassian.net/";
                return false;
            }
        }

        ValidationMessage = string.Empty;
        return true;
    }

    [RelayCommand]
    private void OpenHelpUrl()
    {
        if (string.IsNullOrEmpty(Field.HelpUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(Field.HelpUrl) { UseShellExecute = true });
        }
        catch { /* best-effort */ }
    }
}
