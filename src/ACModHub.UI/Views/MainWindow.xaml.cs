using System.Windows;
using ACModHub.UI.ViewModels;

namespace ACModHub.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
