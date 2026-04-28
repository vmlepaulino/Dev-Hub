using System.Windows;
using DevSprint.UI.ViewModels;

namespace DevSprint.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}