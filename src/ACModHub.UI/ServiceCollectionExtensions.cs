using ACModHub.UI.Services;
using ACModHub.UI.ViewModels;
using ACModHub.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ACModHub.UI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddACModHubUi(this IServiceCollection services)
    {
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IUiErrorHandler, UiErrorHandler>();
        services.AddSingleton<IStorePageBuilder, StorePageBuilder>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<WebView2RuntimeDetector>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CatalogViewModel>();
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<InstallerViewModel>();
        services.AddSingleton<DownloadsViewModel>();
        services.AddTransient<BackupsViewModel>();
        services.AddTransient<UpdatesViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
