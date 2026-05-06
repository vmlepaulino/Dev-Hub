using System.Windows;
using DevSprint.UI.ViewModels;

namespace DevSprint.UI.Views;

/// <summary>
/// Code-behind contains constructor wiring only. Translates the ViewModel's
/// <see cref="OnboardingWizardViewModel.Completed"/> outcome into
/// <see cref="Window.DialogResult"/> so the caller can branch on saved-vs-cancelled.
/// </summary>
public partial class OnboardingWizardDialog : Window
{
    private readonly OnboardingWizardViewModel _viewModel;

    public OnboardingWizardDialog(OnboardingWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        _viewModel.Completed += OnCompleted;
        Closed += OnClosed;
    }

    private void OnCompleted(object? sender, bool saved)
    {
        DialogResult = saved;
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Completed -= OnCompleted;
    }
}
