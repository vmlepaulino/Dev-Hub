using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using CommunityToolkit.Mvvm.Input;
using DevSprint.UI.ViewModels;

namespace DevSprint.UI
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly List<(string Name, IAsyncRelayCommand Command)> _scrollBindings = [];
        private readonly HashSet<ScrollViewer> _wiredScrollViewers = [];

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _scrollBindings.Add(("BacklogScrollViewer", _viewModel.ScrollBacklogCommand));
            _scrollBindings.Add(("SprintScrollViewer", _viewModel.ScrollSprintCommand));
            _scrollBindings.Add(("AssignedScrollViewer", _viewModel.ScrollAssignedCommand));
            _scrollBindings.Add(("ContributingScrollViewer", _viewModel.ScrollContributingCommand));

            WireVisibleScrollViewers();
            MainTabControl.SelectionChanged += (_, _) => WireVisibleScrollViewers();
        }

        private void WireVisibleScrollViewers()
        {
            foreach (var (name, command) in _scrollBindings)
            {
                if (FindName(name) is ScrollViewer sv && _wiredScrollViewers.Add(sv))
                {
                    sv.ScrollChanged += (s, _) => OnScrollNearEnd(s, command);
                }
            }
        }

        private static async void OnScrollNearEnd(object sender, IAsyncRelayCommand command)
        {
            if (sender is not ScrollViewer sv) return;
            if (sv.ScrollableHeight <= 0) return;
            if (sv.VerticalOffset < sv.ScrollableHeight - 100) return;

            if (command.CanExecute(null))
                await command.ExecuteAsync(null);
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}