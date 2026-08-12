using System.IO;
using System.Windows;
using System.Windows.Threading;
using ACModHub.Core.Interfaces;
using ACModHub.Infrastructure;
using ACModHub.Infrastructure.Services;
using ACModHub.UI;
using ACModHub.UI.Services;
using ACModHub.UI.ViewModels;
using ACModHub.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ACModHub.App;

public partial class App : Application
{
    private IHost? _host;
    private ILogger<App>? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ACModHub");
            var builder = Host.CreateApplicationBuilder(e.Args);
            builder.Logging.ClearProviders();
            builder.Logging.AddDebug();
            builder.Logging.AddProvider(new RollingFileLoggerProvider(Path.Combine(dataRoot, "logs")));
            builder.Services.AddACModHubInfrastructure(dataRoot).AddACModHubUi();
            _host = builder.Build();
            await _host.StartAsync();
            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var recovered = await _host.Services.GetRequiredService<ICrashRecoveryService>().RecoverAsync();
            if (recovered > 0) _logger.LogWarning("Recovered {Count} interrupted installation(s)", recovered);
            var settings = await _host.Services.GetRequiredService<ISettingsService>().LoadAsync();
            _host.Services.GetRequiredService<ILocalizationService>().SetLanguage(settings.Language);

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            await _host.Services.GetRequiredService<MainViewModel>().InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Application startup failed");
            MessageBox.Show(ex.Message, "AC Mod Hub — Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try { await _host.StopAsync(TimeSpan.FromSeconds(4)); }
            finally { _host.Dispose(); }
        }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.Exception, "Unhandled UI exception");
        MessageBox.Show(e.Exception.Message, "AC Mod Hub", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
