using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using ACModHub.Core;
using ACModHub.UI.Services;
using ACModHub.UI.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace ACModHub.UI.Views;

/// <summary>
/// Hosts the HTML/CSS/JavaScript store in a hardened WebView2, with a native XAML
/// fallback when the WebView2 runtime is unavailable. The bridge is message-only:
/// JSON schema-validated commands, no host objects, restricted navigation, no DevTools
/// in Release builds, all permissions denied.
/// </summary>
public partial class CatalogView : UserControl
{
    private const string VirtualHost = "acmhub.local";

    private CatalogViewModel? _viewModel;
    private Microsoft.Web.WebView2.Wpf.WebView2? _store;
    private bool _webViewReady;

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
        _viewModel.HtmlPathChanged += OnHtmlPathChanged;
        _viewModel.WebMessage += OnWebMessage;
        if (!string.IsNullOrWhiteSpace(_viewModel.HtmlPath)) OnHtmlPathChanged(_viewModel.HtmlPath);
    }

    private async void OnHtmlPathChanged(string indexPath)
    {
        if (_viewModel is null || !_viewModel.WebViewAvailable) return;
        try
        {
            await EnsureStoreAsync();
            if (_store is not null && File.Exists(indexPath))
            {
                _webViewReady = true;
                _store.CoreWebView2.Navigate(new Uri($"https://{VirtualHost}/index.html").AbsoluteUri);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or CoreWebView2RuntimeNotFoundException or FileNotFoundException)
        {
            // Runtime disappeared or init failed: the native fallback takes over.
        }
    }

    private async Task EnsureStoreAsync()
    {
        if (_store is not null) return;
        var directory = Path.Combine(Path.GetTempPath(), "ACModHub");
        var indexDirectory = Path.GetDirectoryName(_viewModel?.HtmlPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(indexDirectory)) indexDirectory = directory;

        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: directory);
        _store = new Microsoft.Web.WebView2.Wpf.WebView2();
        await _store.EnsureCoreWebView2Async(environment);
        var core = _store.CoreWebView2;

        core.Settings.AreDevToolsEnabled = false;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#endif
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsScriptEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;

        core.SetVirtualHostNameToFolderMapping(VirtualHost, indexDirectory, CoreWebView2HostResourceAccessKind.Allow);
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.PermissionRequested += OnPermissionRequested;
        core.WebMessageReceived += OnWebMessageReceived;

        WebViewContainer.Child = _store;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Only the local virtual host may load. External links open in the default browser
        // after URL validation; everything else is cancelled.
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Host == VirtualHost) return;
        e.Cancel = true;
        if (uri is not null && StoreBridge.IsTrustedExternalUrl(uri.AbsoluteUri)) OpenExternal(uri.AbsoluteUri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (StoreBridge.IsTrustedExternalUrl(e.Uri)) OpenExternal(e.Uri);
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        // The store needs no permissions: deny everything.
        e.State = CoreWebView2PermissionState.Deny;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.TryGetWebMessageAsString();
        if (json is null || _viewModel is null) return;
        if (!StoreBridge.TryParse(json, out var message, out _) || message is null) return;
        switch (message.Command)
        {
            case StoreBridgeCommand.InstallMod:
                if (message.ModId is not null) _viewModel.InstallCommand.Execute(message.ModId);
                break;
            case StoreBridgeCommand.RefreshCatalog:
                _viewModel.RefreshCommand.Execute(null);
                break;
            case StoreBridgeCommand.CancelOperation:
                _viewModel.CancelDownloadCommand.Execute(null);
                break;
            case StoreBridgeCommand.OpenTrustedExternalLink:
                if (message.Url is not null) OpenExternal(message.Url);
                break;
        }
    }

    private void OnWebMessage(string json)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!_webViewReady || _store?.CoreWebView2 is not { } core) return;
            try { core.PostWebMessageAsJson(json); }
            catch (Exception ex) when (ex is InvalidOperationException or COMException) { }
        });
    }

    private static void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Disconnect();

    private void Disconnect()
    {
        if (_viewModel is null) return;
        _viewModel.HtmlPathChanged -= OnHtmlPathChanged;
        _viewModel.WebMessage -= OnWebMessage;
        _viewModel = null;
    }
}
