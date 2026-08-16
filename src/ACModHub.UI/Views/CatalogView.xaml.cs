using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using ACModHub.UI.ViewModels;

namespace ACModHub.UI.Views;

public partial class CatalogView : UserControl
{
    private CatalogViewModel? _viewModel;
    private bool _documentReady;

    public CatalogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Disconnect();
        _viewModel = e.NewValue as CatalogViewModel;
        if (_viewModel is null) return;
        Browser.ObjectForScripting = new CatalogScriptBridge(_viewModel, Dispatcher);
        _viewModel.HtmlChanged += OnHtmlChanged;
        _viewModel.ProgressChanged += OnProgressChanged;
        if (!string.IsNullOrWhiteSpace(_viewModel.Html)) OnHtmlChanged(_viewModel.Html);
    }

    private void OnHtmlChanged(string html)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _documentReady = false;
            Browser.NavigateToString(html);
        });
    }

    private void OnProgressChanged(string json)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!_documentReady) return;
            try { Browser.InvokeScript("catalogProgress", json); }
            catch (Exception) { }
        });
    }

    private void OnLoadCompleted(object sender, NavigationEventArgs e)
    {
        _documentReady = true;
        if (_viewModel?.ProgressJson is { } json) OnProgressChanged(json);
    }

    private void OnNavigating(object sender, NavigatingCancelEventArgs e)
    {
        if (e.Uri is not null && e.Uri.Scheme is not ("about" or "res")) e.Cancel = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Disconnect();

    private void Disconnect()
    {
        if (_viewModel is null) return;
        _viewModel.HtmlChanged -= OnHtmlChanged;
        _viewModel.ProgressChanged -= OnProgressChanged;
        _viewModel = null;
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class CatalogScriptBridge
    {
        private readonly CatalogViewModel _viewModel;
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        public CatalogScriptBridge(CatalogViewModel viewModel, System.Windows.Threading.Dispatcher dispatcher)
        {
            _viewModel = viewModel;
            _dispatcher = dispatcher;
        }

        public void InstallMod(string modId) => _dispatcher.InvokeAsync(() => _viewModel.InstallCommand.Execute(modId));
        public void RefreshCatalog() => _dispatcher.InvokeAsync(() => _viewModel.RefreshCommand.Execute(null));
    }
}
