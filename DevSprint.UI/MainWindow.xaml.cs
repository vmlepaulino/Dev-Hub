using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DevSprint.UI.ViewModels;

namespace DevSprint.UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BacklogScrollViewer.ScrollChanged += OnBacklogScrollChanged;
            MyIssuesScrollViewer.ScrollChanged += OnMyIssuesScrollChanged;
        }

        private async void OnBacklogScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            if (sv.ScrollableHeight <= 0) return;
            if (sv.VerticalOffset < sv.ScrollableHeight - 100) return;

            await _viewModel.ScrollBacklogCommand.ExecuteAsync(null);
        }

        private async void OnMyIssuesScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            if (sv.ScrollableHeight <= 0) return;
            if (sv.VerticalOffset < sv.ScrollableHeight - 100) return;

            await _viewModel.ScrollMyIssuesCommand.ExecuteAsync(null);
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}