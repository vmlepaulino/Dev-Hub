using System.Windows;
using DevSprint.UI.Auth;
using DevSprint.UI.ViewModels;

namespace DevSprint.UI.Views;

/// <summary>
/// Code-behind contains constructor wiring only. Translates the ViewModel's
/// <see cref="JiraSignInViewModel.Completed"/> outcome into
/// <see cref="Window.DialogResult"/> + a <see cref="Tokens"/> property the
/// caller (App.xaml.cs) reads after <see cref="Window.ShowDialog"/>.
/// </summary>
public partial class JiraSignInDialog : Window
{
    private readonly JiraSignInViewModel _viewModel;

    /// <summary>Tokens (with CloudId populated) on success. Null if cancelled or failed.</summary>
    public AuthTokens? Tokens { get; private set; }

    public JiraSignInDialog(JiraSignInViewModel viewModel)
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
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Completed -= OnCompleted;
        if (Tokens is null && _viewModel.CancelCommand.CanExecute(null))
            _viewModel.CancelCommand.Execute(null);
    }
}
