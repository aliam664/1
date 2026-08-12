using ACModHub.Core.Models;

namespace ACModHub.Core.Interfaces;

public interface IArchiveService
{
    Task<ArchiveInspection> InspectAsync(string archivePath, CancellationToken cancellationToken = default);
    Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default);
}

public interface IBackupService
{
    Task<BackupDescriptor> CreateAsync(Guid modId, string gamePath, IEnumerable<string> relativeFiles, CancellationToken cancellationToken = default);
    Task RestoreAsync(BackupDescriptor backup, string gamePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupDescriptor>> GetAllAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid backupId, CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface IJournalStore
{
    Task SaveAsync(InstallationJournal journal, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InstallationJournal>> GetIncompleteAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid journalId, CancellationToken cancellationToken = default);
}

public interface ICrashRecoveryService
{
    Task<int> RecoverAsync(CancellationToken cancellationToken = default);
}

public interface IAppPaths
{
    string DataRoot { get; }
    string ManifestsDirectory { get; }
    string BackupsDirectory { get; }
    string CacheDirectory { get; }
    string DownloadsDirectory { get; }
    string LogsDirectory { get; }
    string JournalsDirectory { get; }
    string DisabledModsDirectory { get; }
}
