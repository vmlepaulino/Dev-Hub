using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevSprint.UI.Onboarding;

namespace DevSprint.UI.ViewModels;

/// <summary>
/// Multi-step setup wizard. Walks the user through one
/// <see cref="OnboardingStepViewModel"/> per <see cref="OnboardingFieldGroup"/>
/// in <see cref="OnboardingCatalog.AllGroups"/>. Saves the collected values to
/// user-secrets via <see cref="IOnboardingService.SaveAsync"/>.
/// </summary>
public sealed partial class OnboardingWizardViewModel : ObservableObject
{
    private readonly IOnboardingService _onboardingService;

    public ObservableCollection<OnboardingStepViewModel> Steps { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(StepIndicator))]
    [NotifyPropertyChangedFor(nameof(IsOnLastStep))]
    [NotifyPropertyChangedFor(nameof(IsOnFirstStep))]
    private int _currentStepIndex;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isCompleted;

    public OnboardingStepViewModel CurrentStep => Steps[CurrentStepIndex];
    public string StepIndicator => $"Step {CurrentStepIndex + 1} of {Steps.Count}";
    public bool IsOnFirstStep => CurrentStepIndex == 0;
    public bool IsOnLastStep => CurrentStepIndex == Steps.Count - 1;

    /// <summary>Raised exactly once when the wizard ends (saved or cancelled).</summary>
    public event EventHandler<bool>? Completed;

    public OnboardingWizardViewModel(IOnboardingService onboardingService, IReadOnlyDictionary<string, string> snapshot)
    {
        _onboardingService = onboardingService;

        foreach (var group in OnboardingCatalog.AllGroups)
            Steps.Add(new OnboardingStepViewModel(group, snapshot));
    }

    private bool CanGoBack() => CurrentStepIndex > 0 && !IsSaving;
    private bool CanGoNext() => CurrentStepIndex < Steps.Count - 1 && !IsSaving;
    private bool CanSave() => CurrentStepIndex == Steps.Count - 1 && !IsSaving;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        StatusMessage = string.Empty;
        if (CurrentStepIndex > 0) CurrentStepIndex--;
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (!CurrentStep.Validate())
        {
            StatusMessage = "Please correct the highlighted fields.";
            return;
        }
        StatusMessage = string.Empty;
        if (CurrentStepIndex < Steps.Count - 1) CurrentStepIndex++;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        // Validate every step (not just the current one) — the user could have
        // modified an earlier step then jumped back forward.
        foreach (var step in Steps)
        {
            if (!step.Validate())
            {
                StatusMessage = $"\"{step.Title}\" has missing or invalid values. Use Back to fix them.";
                return;
            }
        }

        IsSaving = true;
        StatusMessage = "Saving…";
        try
        {
            var values = Steps.SelectMany(s => s.Collect())
                              .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

            await _onboardingService.SaveAsync(values);
            IsCompleted = true;
            Completed?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Completed?.Invoke(this, false);
    }

    [RelayCommand]
    private void OpenRegistrationUrl()
    {
        var url = CurrentStep.RegistrationUrl;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* best-effort */ }
    }
}
