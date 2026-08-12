using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;

namespace ACModHub.Infrastructure.Services;

public sealed class BackupService : IBackupService
{
    private readonly IAppPaths _paths;
    private readonly AtomicJsonStore _store = new();

    public BackupService(IAppPaths paths) => _paths = paths;

    public async Task<BackupDescriptor> CreateAsync(Guid modId, string gamePath, IEnumerable<string> relativeFiles, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentNullException.ThrowIfNull(relativeFiles);
        var backupId = Guid.NewGuid();
        var backup = new BackupDescriptor { Id = backupId, ModId = modId, RootPath = Path.Combine(_paths.BackupsDirectory, backupId.ToString("N")) };
        Directory.CreateDirectory(backup.RootPath);

        try
        {
            foreach (var relative in relativeFiles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = SafePath.NormalizeRelative(relative);
                var source = SafePath.CombineUnderRoot(gamePath, normalized);
                if (!File.Exists(source)) continue;
                var destination = SafePath.CombineUnderRoot(backup.RootPath, normalized);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyAsync(source, destination, false, cancellationToken).ConfigureAwait(false);
                backup.RelativeFiles.Add(normalized.Replace('\\', '/'));
            }
            await _store.WriteAsync(Path.Combine(backup.RootPath, "backup.json"), backup, cancellationToken).ConfigureAwait(false);
            return backup;
        }
        catch
        {
            if (Directory.Exists(backup.RootPath)) Directory.Delete(backup.RootPath, true);
            throw;
        }
    }

    public async Task RestoreAsync(BackupDescriptor backup, string gamePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        foreach (var relative in backup.RelativeFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = SafePath.CombineUnderRoot(backup.RootPath, relative);
            if (!File.Exists(source)) throw new FileNotFoundException("A backup file is missing.", source);
            var target = SafePath.CombineUnderRoot(gamePath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await CopyAsync(source, target, true, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<BackupDescriptor>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var backups = new List<BackupDescriptor>();
        if (!Directory.Exists(_paths.BackupsDirectory)) return backups;
        foreach (var file in Directory.EnumerateFiles(_paths.BackupsDirectory, "backup.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var item = await _store.ReadAsync<BackupDescriptor?>(file, null, cancellationToken).ConfigureAwait(false);
                if (item is not null) backups.Add(item);
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }
        }
        return backups.OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public Task DeleteAsync(Guid backupId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(_paths.BackupsDirectory, backupId.ToString("N"));
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        return Task.CompletedTask;
    }

    private static async Task CopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                await input.CopyToAsync(output, 128 * 1024, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
