using System.Windows;
using System.Windows.Input;
using ACModHub.UI.ViewModels;

namespace ACModHub.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Double-click on the title bar toggles maximize/restore (DPI-aware via WindowChrome).
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
        }
    }
}
