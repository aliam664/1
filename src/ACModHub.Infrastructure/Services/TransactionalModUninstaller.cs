using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

public sealed class TransactionalModUninstaller : IModUninstaller
{
    private readonly IModRepository _repository;
    private readonly IFileHashService _hashes;
    private readonly IBackupService _backups;
    private readonly IJournalStore _journals;
    private readonly IAppPaths _paths;
    private readonly IInstallationLockService _installationLocks;
    private readonly ILogger<TransactionalModUninstaller> _logger;

    public TransactionalModUninstaller(
        IModRepository repository,
        IFileHashService hashes,
        IBackupService backups,
        IJournalStore journals,
        IAppPaths paths,
        IInstallationLockService installationLocks,
        ILogger<TransactionalModUninstaller> logger)
    {
        _repository = repository;
        _hashes = hashes;
        _backups = backups;
        _journals = journals;
        _paths = paths;
        _installationLocks = installationLocks;
        _logger = logger;
    }

    public async Task<UninstallAnalysis> AnalyzeAsync(Guid modId, CancellationToken cancellationToken = default)
    {
        var manifest = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
        var ownership = (await _repository.GetAllOwnershipAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => OwnershipKey(x.RelativePath), StringComparer.OrdinalIgnoreCase);
        return await AnalyzeCoreAsync(manifest, ownership, cancellationToken).ConfigureAwait(false);
    }

    public async Task UninstallAsync(Guid modId, UninstallOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var manifest = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
        var gamePath = RequiredGamePath(manifest);
        await using var installationLock = await _installationLocks.AcquireAsync(gamePath, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Uninstall started for mod {ModId} in game root {GameRoot}", modId, gamePath);
        var ownershipByPath = (await _repository.GetAllOwnershipAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => OwnershipKey(x.RelativePath), StringComparer.OrdinalIgnoreCase);
        var analysis = await AnalyzeCoreAsync(manifest, ownershipByPath, cancellationToken).ConfigureAwait(false);
        ValidateDecision(analysis, options);

        var modified = analysis.ModifiedFiles.Select(x => OwnershipKey(x.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingBackups = manifest.Files.Where(file => IsTopOwner(ownershipByPath.GetValueOrDefault(OwnershipKey(file.RelativePath)), modId)
            && !(modified.Contains(OwnershipKey(file.RelativePath)) && options.ModifiedFileAction == ModifiedFileAction.Preserve)
            && file.WasExisting && (string.IsNullOrWhiteSpace(file.BackupPath) || !File.Exists(file.BackupPath))).Select(x => x.RelativePath).ToArray();
        if (missingBackups.Length > 0)
            throw new ModHubException($"Uninstall was stopped because {missingBackups.Length} required pre-install backup file(s) are missing. No game files were changed.");

        var pathsChangedInGame = manifest.Files
            .Where(file => IsTopOwner(ownershipByPath.GetValueOrDefault(OwnershipKey(file.RelativePath)), modId)
                           && !(modified.Contains(OwnershipKey(file.RelativePath)) && options.ModifiedFileAction == ModifiedFileAction.Preserve))
            .Select(file => file.RelativePath)
            .Where(relative => File.Exists(SafePath.CombineUnderRoot(gamePath, relative)))
            .ToArray();
        var rollbackBackup = await _backups.CreateAsync(modId, gamePath, pathsChangedInGame, cancellationToken).ConfigureAwait(false);
        var journal = new InstallationJournal
        {
            ModId = modId,
            GamePath = gamePath,
            ArchivePath = manifest.SourceArchivePath ?? "uninstall",
            Kind = TransactionKind.Uninstall,
            PreviousManifest = manifest,
            PreviousOwnership = ownershipByPath.Values.Where(x => x.ModIds.Contains(modId)).Select(CloneOwnership).ToList(),
            Stage = InstallStage.Install
        };
        await _journals.SaveAsync(journal, cancellationToken).ConfigureAwait(false);

        try
        {
            var upserts = new List<FileOwnershipRecord>();
            var deletions = new List<string>();
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = OwnershipKey(file.RelativePath);
                if (!ownershipByPath.TryGetValue(key, out var ownership)) continue;
                NormalizeOwnerStack(ownership);
                var preserveModified = modified.Contains(key) && options.ModifiedFileAction == ModifiedFileAction.Preserve;
                var target = SafePath.CombineUnderRoot(gamePath, file.RelativePath);
                if (IsTopOwner(ownership, modId) && !preserveModified)
                    await RestoreOrDeleteFileAsync(file, target, gamePath, rollbackBackup, journal, cancellationToken).ConfigureAwait(false);

                ownership.ModIds.Remove(modId);
                ownership.OwnerStack.RemoveAll(x => x == modId);
                if (ownership.ModIds.Count == 0) deletions.Add(file.RelativePath); else upserts.Add(ownership);
            }

            await _repository.ApplyOwnershipChangesAsync(upserts, deletions, cancellationToken).ConfigureAwait(false);
            await _repository.DeleteAsync(modId, cancellationToken).ConfigureAwait(false);
            journal.State = JournalState.Completed;
            journal.Stage = InstallStage.Done;
            await _journals.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
            CleanupModCaches(manifest, modified.Count > 0 && options.ModifiedFileAction == ModifiedFileAction.Preserve);
            _logger.LogInformation("Uninstall completed for mod {ModId} in game root {GameRoot}", modId, gamePath);
        }
        catch
        {
            var rolledBack = Rollback(journal);
            await _repository.ApplyOwnershipChangesAsync(journal.PreviousOwnership.Select(CloneOwnership), [], CancellationToken.None).ConfigureAwait(false);
            await _repository.SaveAsync(manifest, CancellationToken.None).ConfigureAwait(false);
            journal.State = rolledBack ? JournalState.RolledBack : JournalState.RecoveryRequired;
            await _journals.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<UninstallAnalysis> AnalyzeCoreAsync(
        ModManifest manifest,
        IReadOnlyDictionary<string, FileOwnershipRecord> ownershipByPath,
        CancellationToken cancellationToken)
    {
        var gamePath = RequiredGamePath(manifest);
        var modified = new List<ModifiedInstalledFile>();
        var blockers = new HashSet<Guid>();
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ownershipByPath.TryGetValue(OwnershipKey(file.RelativePath), out var ownership);
            if (ownership is not null)
            {
                NormalizeOwnerStack(ownership);
                var ownerIndex = ownership.OwnerStack.IndexOf(manifest.Id);
                if (ownerIndex >= 0)
                    foreach (var newerOwner in ownership.OwnerStack.Skip(ownerIndex + 1)) blockers.Add(newerOwner);
            }
            if (manifest.Status != ModStatus.Disabled && !IsTopOwner(ownership, manifest.Id)) continue;
            var currentPath = manifest.Status == ModStatus.Disabled && !string.IsNullOrWhiteSpace(file.DisabledStorePath)
                ? file.DisabledStorePath!
                : SafePath.CombineUnderRoot(gamePath, file.RelativePath);
            if (!File.Exists(currentPath) || string.IsNullOrWhiteSpace(file.Sha256)) continue;
            var currentHash = await _hashes.ComputeSha256Async(currentPath, cancellationToken).ConfigureAwait(false);
            if (!currentHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                modified.Add(new ModifiedInstalledFile(file.RelativePath, file.Sha256, currentHash));
        }
        return new UninstallAnalysis { ModId = manifest.Id, ModifiedFiles = modified, BlockingNewerModIds = blockers };
    }

    private void ValidateDecision(UninstallAnalysis analysis, UninstallOptions options)
    {
        if (!analysis.CanUninstall)
            throw new ModHubException($"This mod cannot be uninstalled before {analysis.BlockingNewerModIds.Count} newer mod(s) that override the same files. Uninstall the newer mods first.");
        if (analysis.RequiresUserDecision)
            _logger.LogWarning("Uninstall for mod {ModId} found {ModifiedFileCount} modified file(s); selected action is {ModifiedFileAction}", analysis.ModId, analysis.ModifiedFiles.Count, options.ModifiedFileAction);
        if (analysis.RequiresUserDecision && options.ModifiedFileAction == ModifiedFileAction.Abort)
            throw new ModHubException($"{analysis.ModifiedFiles.Count} installed file(s) were modified after installation. Choose whether to preserve them or restore the pre-install state.");
    }

    private async Task RestoreOrDeleteFileAsync(ModFileRecord file, string target, string gamePath, BackupDescriptor rollbackBackup, InstallationJournal journal, CancellationToken cancellationToken)
    {
        var targetExisted = File.Exists(target);
        var rollbackPath = targetExisted && rollbackBackup.RelativeFiles.Contains(file.RelativePath, StringComparer.OrdinalIgnoreCase)
            ? SafePath.CombineUnderRoot(rollbackBackup.RootPath, file.RelativePath)
            : null;
        var operation = targetExisted
            ? new JournalOperation { Kind = file.WasExisting ? FileOperationKind.Replaced : FileOperationKind.Deleted, TargetRelativePath = file.RelativePath, BackupPath = rollbackPath }
            : new JournalOperation { Kind = FileOperationKind.Created, TargetRelativePath = file.RelativePath };
        await _journals.AppendOperationAsync(journal, operation, cancellationToken).ConfigureAwait(false);

        var originalBackup = file.BackupPath;
        if (file.WasExisting && !string.IsNullOrWhiteSpace(originalBackup) && File.Exists(originalBackup))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await AtomicCopyAsync(originalBackup, target, cancellationToken).ConfigureAwait(false);
        }
        else if (File.Exists(target))
        {
            File.Delete(target);
            DeleteEmptyParents(Path.GetDirectoryName(target), gamePath);
        }
    }

    private void CleanupModCaches(ModManifest manifest, bool preserveDisabledChanges)
    {
        var disabledRoot = Path.Combine(_paths.DisabledModsDirectory, manifest.Id.ToString("N"));
        try { if (!(manifest.Status == ModStatus.Disabled && preserveDisabledChanges) && Directory.Exists(disabledRoot)) Directory.Delete(disabledRoot, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _logger.LogWarning(ex, "Could not remove disabled-file cache for mod {ModId}", manifest.Id); }
        if (!string.IsNullOrWhiteSpace(manifest.SourceArchivePath)) TryDeleteCachedPackage(manifest.SourceArchivePath);
    }

    private bool Rollback(InstallationJournal journal)
    {
        try
        {
            foreach (var operation in journal.Operations.AsEnumerable().Reverse())
            {
                var target = SafePath.CombineUnderRoot(journal.GamePath, operation.TargetRelativePath);
                if (operation.Kind == FileOperationKind.Created) { if (File.Exists(target)) File.Delete(target); }
                else if (operation.Kind is FileOperationKind.Replaced or FileOperationKind.Deleted)
                {
                    if (string.IsNullOrWhiteSpace(operation.BackupPath) || !File.Exists(operation.BackupPath)) return false;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(operation.BackupPath, target, true);
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogCritical(ex, "Uninstall rollback failed for journal {JournalId}", journal.Id);
            return false;
        }
    }

    private async Task<ModManifest> RequiredManifestAsync(Guid modId, CancellationToken cancellationToken) =>
        await _repository.GetAsync(modId, cancellationToken).ConfigureAwait(false) ?? throw new ModHubException("The selected mod is not installed.");

    private static string RequiredGamePath(ModManifest manifest) =>
        manifest.Metadata.TryGetValue("gamePath", out var path) && !string.IsNullOrWhiteSpace(path) ? path : throw new ModHubException("The mod manifest does not contain its game path.");

    private static async Task AtomicCopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        var temporary = target + ".acmodhub-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                await input.CopyToAsync(output, 128 * 1024, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void TryDeleteCachedPackage(string path)
    {
        try
        {
            var packageRoot = Path.GetFullPath(Path.Combine(_paths.CacheDirectory, "packages")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _logger.LogWarning(ex, "Could not remove cached package {PackagePath}", path); }
    }

    private static bool IsTopOwner(FileOwnershipRecord? ownership, Guid modId)
    {
        if (ownership is null || !ownership.ModIds.Contains(modId)) return false;
        NormalizeOwnerStack(ownership);
        return ownership.OwnerStack.Count == 0 || ownership.OwnerStack[^1] == modId;
    }

    private static void NormalizeOwnerStack(FileOwnershipRecord ownership)
    {
        ownership.OwnerStack.RemoveAll(x => !ownership.ModIds.Contains(x));
        foreach (var owner in ownership.ModIds)
            if (!ownership.OwnerStack.Contains(owner)) ownership.OwnerStack.Add(owner);
    }

    private static FileOwnershipRecord CloneOwnership(FileOwnershipRecord source) => new()
    {
        RelativePath = source.RelativePath,
        ModIds = new HashSet<Guid>(source.ModIds),
        OwnerStack = new List<Guid>(source.OwnerStack),
        UpdatedAt = source.UpdatedAt
    };

    private static string OwnershipKey(string path) => path.Replace('\\', '/').Trim('/');

    private static void DeleteEmptyParents(string? directory, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        while (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) && !Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any()) break;
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }
}
