using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Services;
using ACModHub.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddACModHubInfrastructure(this IServiceCollection services, string? dataRoot = null)
    {
        // Central configuration (endpoints, limits, timeouts) — single instance.
        services.AddSingleton(AppConfig.Production);

        services.AddSingleton<IAppPaths>(_ => new AppPaths(dataRoot));
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IModRepository, JsonModRepository>();
        services.AddSingleton<IJournalStore, JsonJournalStore>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IArchiveService, SecureArchiveService>();
        services.AddSingleton<IFileHashService, FileHashService>();
        services.AddSingleton<IDiskSpaceService, DiskSpaceService>();
        services.AddSingleton<IFileLockService, FileLockService>();
        services.AddSingleton<IInstallationLockService, InstallationLockService>();
        services.AddSingleton<IUserErrorMessageService, UserErrorMessageService>();
        services.AddSingleton<ISteamLocationProvider, RegistrySteamLocationProvider>();
        services.AddSingleton<IGameDetector, SteamGameDetector>();
        services.AddSingleton<IModStructureDetector, ModStructureDetector>();
        services.AddSingleton<IConflictDetector, ConflictDetector>();
        services.AddSingleton<IManifestService, ManifestService>();
        services.AddSingleton<IModScanner, ModScanner>();
        services.AddSingleton<IModUninstaller, TransactionalModUninstaller>();
        services.AddSingleton<IModInstaller, TransactionalModInstaller>();
        services.AddSingleton<ICrashRecoveryService, CrashRecoveryService>();
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<IContentProvider, DirectUrlContentProvider>();
        services.AddSingleton<IModCatalogService, JsonModCatalogService>();
        services.AddSingleton<ICoverImageService, CoverImageService>();
        services.AddSingleton<IUpdateChecker, GitHubUpdateChecker>();
        services.AddSingleton<IUpdateDownloader, UpdateDownloader>();
        services.AddSingleton<IUpdateApplier, UpdateApplier>();

        services.AddHttpClient("catalog", (provider, client) =>
        {
            client.Timeout = provider.GetRequiredService<AppConfig>().MetadataTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        });
        services.AddHttpClient("updates", (provider, client) =>
        {
            client.Timeout = provider.GetRequiredService<AppConfig>().MetadataTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });
        services.AddHttpClient("covers", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        });
        services.AddHttpClient("downloads", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        });

        services.AddSingleton<ConcurrencySettings>();
        services.AddSingleton<IDownloadManager>(provider => new HttpDownloadManager(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("downloads"),
            provider.GetRequiredService<IFileHashService>(),
            provider.GetRequiredService<IAppPaths>(),
            provider.GetRequiredService<ILogger<HttpDownloadManager>>(),
            () => provider.GetRequiredService<ConcurrencySettings>().ConcurrentDownloads));
        return services;
    }
}
