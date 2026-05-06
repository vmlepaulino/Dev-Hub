using System.Windows;
using DevSprint.UI.Auth;
using DevSprint.UI.ViewModels;

namespace DevSprint.UI.Views;

/// <summary>
/// Code-behind contains constructor wiring only. Uses
/// <see cref="DeviceCodeViewModel.Completed"/> to translate the VM's outcome
/// into <see cref="Window.DialogResult"/> + a <see cref="Tokens"/> property
/// the caller (App.xaml.cs) can read after <see cref="Window.ShowDialog"/>.
/// </summary>
public partial class DeviceCodeDialog : Window
{
    private readonly DeviceCodeViewModel _viewModel;

    /// <summary>Tokens obtained on success. Null if the user cancelled or sign-in failed.</summary>
    public AuthTokens? Tokens { get; private set; }

    public DeviceCodeDialog(DeviceCodeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        _viewModel.Completed += OnCompleted;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.StartAsync();
    }

    private void OnCompleted(object? sender, AuthTokens? tokens)
    {
        Tokens = tokens;
        DialogResult = tokens is not null;
        // Close on UI thread — Completed may fire from a background continuation.
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Completed -= OnCompleted;
        // If the user closed via the X button before completion, ensure cancellation.
        if (Tokens is null && _viewModel.CancelCommand.CanExecute(null))
            _viewModel.CancelCommand.Execute(null);
    }
}
