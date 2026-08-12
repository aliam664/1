using ACModHub.Core.Interfaces;
using ACModHub.Core.Services;
using ACModHub.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ACModHub.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddACModHubInfrastructure(this IServiceCollection services, string? dataRoot = null)
    {
        services.AddSingleton<IAppPaths>(_ => new AppPaths(dataRoot));
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IModRepository, JsonModRepository>();
        services.AddSingleton<IJournalStore, JsonJournalStore>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IArchiveService, SecureArchiveService>();
        services.AddSingleton<IFileHashService, FileHashService>();
        services.AddSingleton<IDiskSpaceService, DiskSpaceService>();
        services.AddSingleton<IFileLockService, FileLockService>();
        services.AddSingleton<ISteamLocationProvider, RegistrySteamLocationProvider>();
        services.AddSingleton<IGameDetector, SteamGameDetector>();
        services.AddSingleton<IModStructureDetector, ModStructureDetector>();
        services.AddSingleton<IConflictDetector, ConflictDetector>();
        services.AddSingleton<IManifestService, ManifestService>();
        services.AddSingleton<IModScanner, ModScanner>();
        services.AddSingleton<IModInstaller, TransactionalModInstaller>();
        services.AddSingleton<ICrashRecoveryService, CrashRecoveryService>();
        services.AddSingleton<ILaunchService, LaunchService>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<IContentProvider, DirectUrlContentProvider>();
        services.AddHttpClient("downloads", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ACModHub/1.0");
        });
        services.AddSingleton<IDownloadManager>(provider => new HttpDownloadManager(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("downloads"),
            provider.GetRequiredService<IFileHashService>(),
            provider.GetRequiredService<IAppPaths>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HttpDownloadManager>>(),
            provider.GetRequiredService<ISettingsService>().LoadAsync().GetAwaiter().GetResult().ConcurrentDownloads));
        return services;
    }
}
