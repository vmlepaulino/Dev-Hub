using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DevSprint.UI.Onboarding;

namespace DevSprint.UI.ViewModels;

/// <summary>
/// One step of the wizard — corresponds to an <see cref="OnboardingFieldGroup"/>
/// (e.g. "Backlog Management" or "Code Repository"). Holds the row VMs for the
/// step's fields and handles step-level validation.
/// </summary>
public sealed partial class OnboardingStepViewModel : ObservableObject
{
    public OnboardingFieldGroup Group { get; }
    public ObservableCollection<OnboardingFieldRowViewModel> Rows { get; } = new();

    public string Title => Group.Title;
    public string Description => Group.Description;
    public string? RegistrationUrl => Group.RegistrationUrl;
    public string? PostStepNote => Group.PostStepNote;
    public bool HasPostStepNote => !string.IsNullOrEmpty(Group.PostStepNote);

    public OnboardingStepViewModel(OnboardingFieldGroup group, IReadOnlyDictionary<string, string> snapshot)
    {
        Group = group;
        foreach (var field in group.Fields)
        {
            var initial = snapshot.TryGetValue(field.Key, out var value) ? value : string.Empty;
            Rows.Add(new OnboardingFieldRowViewModel(field, initial));
        }
    }

    /// <summary>Validates every row; returns true only if all pass.</summary>
    public bool Validate()
    {
        var allValid = true;
        foreach (var row in Rows)
        {
            if (!row.Validate()) allValid = false;
        }
        return allValid;
    }

    /// <summary>Flat key→value pairs for everything in this step. Empty strings are kept (caller filters).</summary>
    public IEnumerable<KeyValuePair<string, string>> Collect() =>
        Rows.Select(r => new KeyValuePair<string, string>(r.Field.Key, (r.Value ?? string.Empty).Trim()));
}
