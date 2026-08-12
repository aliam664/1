using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

public sealed class TransactionalModInstaller : IModInstaller
{
    private readonly IArchiveService _archives;
    private readonly IModStructureDetector _structures;
    private readonly IConflictDetector _conflicts;
    private readonly IBackupService _backups;
    private readonly IModRepository _repository;
    private readonly IManifestService _manifests;
    private readonly IFileHashService _hashes;
    private readonly IDiskSpaceService _diskSpace;
    private readonly IJournalStore _journals;
    private readonly IAppPaths _paths;
    private readonly ILogger<TransactionalModInstaller> _logger;
    private readonly SemaphoreSlim _installationGate = new(1, 1);

    public TransactionalModInstaller(
        IArchiveService archives,
        IModStructureDetector structures,
        IConflictDetector conflicts,
        IBackupService backups,
        IModRepository repository,
        IManifestService manifests,
        IFileHashService hashes,
        IDiskSpaceService diskSpace,
        IJournalStore journals,
        IAppPaths paths,
        ILogger<TransactionalModInstaller> logger)
    {
        _archives = archives;
        _structures = structures;
        _conflicts = conflicts;
        _backups = backups;
        _repository = repository;
        _manifests = manifests;
        _hashes = hashes;
        _diskSpace = diskSpace;
        _journals = journals;
        _paths = paths;
        _logger = logger;
    }

    public Task<ModAnalysis> AnalyzeAsync(string archivePath, string gamePath, CancellationToken cancellationToken = default) =>
        AnalyzeCoreAsync(archivePath, gamePath, null, cancellationToken);

    public Task<InstallResult> InstallAsync(ModAnalysis analysis, InstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
        InstallCoreAsync(analysis, options, Guid.NewGuid(), null, progress, cancellationToken);

    public async Task<InstallResult> UpdateAsync(Guid modId, string archivePath, InstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var current = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
        var gamePath = RequiredGamePath(current);
        var analysis = await AnalyzeCoreAsync(archivePath, gamePath, modId, cancellationToken).ConfigureAwait(false);
        return await InstallCoreAsync(analysis, options, modId, current, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<InstallResult> ReinstallAsync(Guid modId, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var current = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(current.SourceArchivePath) || !File.Exists(current.SourceArchivePath))
            return new InstallResult { Success = false, ModId = modId, Error = "The cached source package is not available." };
        return await UpdateAsync(modId, current.SourceArchivePath, new InstallOptions { AllowOverwriteConflicts = true }, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VerificationResult> RepairAsync(Guid modId, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var current = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
        var initial = await _manifests.VerifyAsync(current, RequiredGamePath(current), cancellationToken).ConfigureAwait(false);
        if (initial.IsHealthy) return initial;
        var reinstall = await ReinstallAsync(modId, progress, cancellationToken).ConfigureAwait(false);
        if (!reinstall.Success) return initial;
        current = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
        return await _manifests.VerifyAsync(current, RequiredGamePath(current), cancellationToken).ConfigureAwait(false);
    }

    public async Task DisableAsync(Guid modId, CancellationToken cancellationToken = default)
    {
        await _installationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
            if (manifest.Status == ModStatus.Disabled) return;
            var gamePath = RequiredGamePath(manifest);
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ownership = await _repository.GetOwnershipAsync(file.RelativePath, cancellationToken).ConfigureAwait(false);
                if (ownership is null || ownership.ModIds.Count != 1 || !ownership.ModIds.Contains(modId)) continue;
                var source = SafePath.CombineUnderRoot(gamePath, file.RelativePath);
                if (!File.Exists(source)) continue;
                var disabledRoot = Path.Combine(_paths.DisabledModsDirectory, modId.ToString("N"));
                var destination = SafePath.CombineUnderRoot(disabledRoot, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination, true);
                file.DisabledStorePath = destination;
            }
            manifest.Status = ModStatus.Disabled;
            manifest.UpdatedAt = DateTimeOffset.UtcNow;
            await _repository.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
        }
        finally { _installationGate.Release(); }
    }

    public async Task EnableAsync(Guid modId, CancellationToken cancellationToken = default)
    {
        await _installationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
            if (manifest.Status == ModStatus.Enabled) return;
            var gamePath = RequiredGamePath(manifest);
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(file.DisabledStorePath) || !File.Exists(file.DisabledStorePath)) continue;
                var ownership = await _repository.GetOwnershipAsync(file.RelativePath, cancellationToken).ConfigureAwait(false);
                var target = SafePath.CombineUnderRoot(gamePath, file.RelativePath);
                if (ownership is not null && ownership.ModIds.Count > 1 && File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file.DisabledStorePath, target, true);
                file.DisabledStorePath = null;
            }
            manifest.Status = ModStatus.Enabled;
            manifest.UpdatedAt = DateTimeOffset.UtcNow;
            await _repository.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
        }
        finally { _installationGate.Release(); }
    }

    public async Task UninstallAsync(Guid modId, CancellationToken cancellationToken = default)
    {
        await _installationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
            var gamePath = RequiredGamePath(manifest);
            var soleOwned = new List<string>();
            foreach (var file in manifest.Files)
            {
                var ownership = await _repository.GetOwnershipAsync(file.RelativePath, cancellationToken).ConfigureAwait(false);
                if (ownership is not null && ownership.ModIds.SetEquals([modId])) soleOwned.Add(file.RelativePath);
            }
            var backup = await _backups.CreateAsync(modId, gamePath, soleOwned, cancellationToken).ConfigureAwait(false);
            var journal = new InstallationJournal { ModId = modId, GamePath = gamePath, ArchivePath = manifest.SourceArchivePath ?? "uninstall", Stage = InstallStage.Install };
            foreach (var relative in backup.RelativeFiles)
                journal.Operations.Add(new JournalOperation { Kind = FileOperationKind.Deleted, TargetRelativePath = relative, BackupPath = SafePath.CombineUnderRoot(backup.RootPath, relative) });
            await _journals.SaveAsync(journal, cancellationToken).ConfigureAwait(false);

            try
            {
                foreach (var file in manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var ownership = await _repository.GetOwnershipAsync(file.RelativePath, cancellationToken).ConfigureAwait(false);
                    if (ownership is not null)
                    {
                        ownership.ModIds.Remove(modId);
                        if (ownership.ModIds.Count == 0)
                        {
                            var target = SafePath.CombineUnderRoot(gamePath, file.RelativePath);
                            if (File.Exists(target)) File.Delete(target);
                            await _repository.DeleteOwnershipAsync(file.RelativePath, cancellationToken).ConfigureAwait(false);
                            DeleteEmptyParents(Path.GetDirectoryName(target), gamePath);
                        }
                        else await _repository.SaveOwnershipAsync(ownership, cancellationToken).ConfigureAwait(false);
                    }
                    if (!string.IsNullOrWhiteSpace(file.DisabledStorePath) && File.Exists(file.DisabledStorePath)) File.Delete(file.DisabledStorePath);
                }
                await _repository.DeleteAsync(modId, cancellationToken).ConfigureAwait(false);
                journal.State = JournalState.Completed;
                journal.Stage = InstallStage.Done;
                await _journals.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await _backups.RestoreAsync(backup, gamePath, CancellationToken.None).ConfigureAwait(false);
                foreach (var file in manifest.Files)
                {
                    var ownership = await _repository.GetOwnershipAsync(file.RelativePath, CancellationToken.None).ConfigureAwait(false)
                        ?? new FileOwnershipRecord { RelativePath = file.RelativePath };
                    ownership.ModIds.Add(modId);
                    await _repository.SaveOwnershipAsync(ownership, CancellationToken.None).ConfigureAwait(false);
                }
                await _repository.SaveAsync(manifest, CancellationToken.None).ConfigureAwait(false);
                journal.State = JournalState.RolledBack;
                await _journals.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally { _installationGate.Release(); }
    }

    private async Task<ModAnalysis> AnalyzeCoreAsync(string archivePath, string gamePath, Guid? currentModId, CancellationToken cancellationToken)
    {
        var fullGamePath = Path.GetFullPath(gamePath);
        if (!Directory.Exists(fullGamePath)) throw new DirectoryNotFoundException("The selected game directory does not exist.");
        var inspection = await _archives.InspectAsync(archivePath, cancellationToken).ConfigureAwait(false);
        var plan = _structures.Detect(Path.GetFileName(archivePath), inspection.Entries);
        var conflicts = (await _conflicts.DetectAsync(fullGamePath, plan.Files, currentModId, cancellationToken).ConfigureAwait(false)).ToList();
        var existingBytes = plan.Files.Select(x => SafePath.CombineUnderRoot(fullGamePath, x.DestinationPath)).Where(File.Exists).Sum(x => new FileInfo(x).Length);
        var required = checked(inspection.TotalUncompressedSize + existingBytes + 64L * 1024 * 1024);
        try
        {
            if (_diskSpace.GetAvailableBytes(fullGamePath) < required)
                conflicts.Add(new(".", ConflictKind.InsufficientSpace, $"At least {required:N0} bytes are required."));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Disk space could not be determined for {GamePath}", fullGamePath);
        }
        return new ModAnalysis { ArchivePath = inspection.ArchivePath, GamePath = fullGamePath, Plan = plan, Conflicts = conflicts, RequiredDiskBytes = required };
    }

    private async Task<InstallResult> InstallCoreAsync(ModAnalysis analysis, InstallOptions options, Guid modId, ModManifest? previous, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(options);
        var gamePath = previous is null ? analysis.GamePath : RequiredGamePath(previous);
        var blocking = analysis.Conflicts.Where(x => (x.Kind is ConflictKind.UnsafePath or ConflictKind.LockedFile or ConflictKind.InsufficientSpace) || !options.AllowOverwriteConflicts).ToArray();
        if (blocking.Length > 0)
            return new InstallResult { Success = false, ModId = modId, Error = string.Join(Environment.NewLine, blocking.Take(5).Select(x => x.Message)) };

        await _installationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var staging = Path.Combine(_paths.CacheDirectory, "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var journal = new InstallationJournal { ModId = modId, GamePath = gamePath, ArchivePath = analysis.ArchivePath };
        BackupDescriptor? backup = null;
        try
        {
            Report(progress, InstallStage.Analyze, 0, "Validating archive and destination");
            await _journals.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
            await _archives.ExtractAsync(analysis.ArchivePath, staging, cancellationToken).ConfigureAwait(false);

            var backupCandidates = analysis.Plan.Files.Select(x => x.DestinationPath)
                .Concat(previous?.Files.Select(x => x.RelativePath) ?? Enumerable.Empty<string>())
                .Where(x => File.Exists(SafePath.CombineUnderRoot(gamePath, x)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (backupCandidates.Length > 0 && !options.CreateBackup) throw new InstallationException("Overwriting or removing existing files requires a backup.");
            journal.Stage = InstallStage.Backup;
            Report(progress, InstallStage.Backup, 0.12, "Backing up existing files");
            backup = await _backups.CreateAsync(modId, gamePath, backupCandidates, cancellationToken).ConfigureAwait(false);

            journal.Stage = InstallStage.Install;
            var records = new List<ModFileRecord>(analysis.Plan.Files.Count);
            for (var i = 0; i < analysis.Plan.Files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var planned = analysis.Plan.Files[i];
                var source = SafePath.CombineUnderRoot(staging, planned.ArchivePath);
                if (!File.Exists(source)) throw new InstallationException($"Extracted source is missing: {planned.ArchivePath}");
                var target = SafePath.CombineUnderRoot(gamePath, planned.DestinationPath);
                var existed = File.Exists(target);
                var backupPath = existed && backup is not null ? SafePath.CombineUnderRoot(backup.RootPath, planned.DestinationPath) : null;
                journal.Operations.Add(new JournalOperation { Kind = existed ? FileOperationKind.Replaced : FileOperationKind.Created, TargetRelativePath = planned.DestinationPath, BackupPath = backupPath });
                await _journals.SaveAsync(journal, cancellationToken).ConfigureAwait(false);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await AtomicCopyAsync(source, target, cancellationToken).ConfigureAwait(false);
                var hash = await _hashes.ComputeSha256Async(target, cancellationToken).ConfigureAwait(false);
                records.Add(new ModFileRecord { RelativePath = planned.DestinationPath.Replace('\\', '/'), Size = new FileInfo(target).Length, Sha256 = hash });
                Report(progress, InstallStage.Install, 0.15 + 0.65 * (i + 1d) / analysis.Plan.Files.Count, "Installing files", planned.DestinationPath);
            }

            await RemoveObsoleteFilesAsync(previous, records, gamePath, journal, backup, cancellationToken).ConfigureAwait(false);
            var package = await CachePackageAsync(analysis.ArchivePath, modId, cancellationToken).ConfigureAwait(false);
            var manifest = new ModManifest
            {
                Id = modId,
                Name = options.NameOverride ?? analysis.Plan.SuggestedName,
                Author = options.AuthorOverride ?? analysis.Plan.Author,
                Version = options.VersionOverride ?? analysis.Plan.Version,
                Category = analysis.Plan.Category,
                Status = ModStatus.Enabled,
                Size = records.Sum(x => x.Size),
                SourceArchivePath = package,
                InstalledAt = previous?.InstalledAt ?? DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Files = records
            };
            manifest.Metadata["gamePath"] = gamePath;
            manifest.Metadata["originalArchive"] = analysis.ArchivePath;

            journal.Stage = InstallStage.Verify;
            Report(progress, InstallStage.Verify, 0.84, "Verifying SHA-256 hashes");
            if (options.VerifyAfterInstall)
            {
                var verification = await _manifests.VerifyAsync(manifest, gamePath, cancellationToken).ConfigureAwait(false);
                if (!verification.IsHealthy) throw new InstallationException($"Verification failed for {verification.Issues.Count} file(s).");
            }

            await SaveOwnershipAsync(manifest, previous, cancellationToken).ConfigureAwait(false);
            await _repository.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
            journal.State = JournalState.Completed;
            journal.Stage = InstallStage.Done;
            await _journals.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
            Report(progress, InstallStage.Done, 1, "Installation completed");
            return new InstallResult { Success = true, ModId = modId };
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            _logger.LogError(ex, "Installation {JournalId} failed; rollback started", journal.Id);
            Report(progress, InstallStage.RollingBack, 0, "Rolling back changes");
            var rolledBack = Rollback(journal);
            await RemoveOwnerReferencesAsync(modId, CancellationToken.None).ConfigureAwait(false);
            if (previous is not null)
            {
                await RestoreOwnerReferencesAsync(previous, CancellationToken.None).ConfigureAwait(false);
                await _repository.SaveAsync(previous, CancellationToken.None).ConfigureAwait(false);
            }
            journal.State = rolledBack ? JournalState.RolledBack : JournalState.RecoveryRequired;
            journal.Stage = InstallStage.Failed;
            journal.Error = ex.Message;
            await _journals.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
            return new InstallResult { Success = false, ModId = modId, Error = ex.Message, WasRolledBack = rolledBack };
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { }
            _installationGate.Release();
        }
    }

    private async Task<ModManifest> RequiredManifestAsync(Guid modId, CancellationToken cancellationToken) =>
        await _repository.GetAsync(modId, cancellationToken).ConfigureAwait(false) ?? throw new ModHubException("The selected mod is not installed.");

    private static string RequiredGamePath(ModManifest manifest) =>
        manifest.Metadata.TryGetValue("gamePath", out var path) && !string.IsNullOrWhiteSpace(path) ? path : throw new ModHubException("The mod manifest does not contain its game path.");

    private static void Report(IProgress<InstallProgress>? progress, InstallStage stage, double percentage, string message, string? file = null) =>
        progress?.Report(new(stage, percentage, message, file));

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

    private async Task<string> CachePackageAsync(string archivePath, Guid modId, CancellationToken cancellationToken)
    {
        var packages = Path.Combine(_paths.CacheDirectory, "packages");
        Directory.CreateDirectory(packages);
        var destination = Path.Combine(packages, modId.ToString("N") + Path.GetExtension(archivePath).ToLowerInvariant());
        await AtomicCopyAsync(archivePath, destination, cancellationToken).ConfigureAwait(false);
        return destination;
    }

    private async Task SaveOwnershipAsync(ModManifest manifest, ModManifest? previous, CancellationToken cancellationToken)
    {
        if (previous is not null)
        {
            var currentPaths = manifest.Files.Select(x => x.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var old in previous.Files.Where(x => !currentPaths.Contains(x.RelativePath)))
            {
                var owner = await _repository.GetOwnershipAsync(old.RelativePath, cancellationToken).ConfigureAwait(false);
                if (owner is null) continue;
                owner.ModIds.Remove(manifest.Id);
                if (owner.ModIds.Count == 0) await _repository.DeleteOwnershipAsync(old.RelativePath, cancellationToken).ConfigureAwait(false);
                else await _repository.SaveOwnershipAsync(owner, cancellationToken).ConfigureAwait(false);
            }
        }
        foreach (var file in manifest.Files)
        {
            var owner = await _repository.GetOwnershipAsync(file.RelativePath, cancellationToken).ConfigureAwait(false) ?? new FileOwnershipRecord { RelativePath = file.RelativePath };
            owner.ModIds.Add(manifest.Id);
            owner.UpdatedAt = DateTimeOffset.UtcNow;
            await _repository.SaveOwnershipAsync(owner, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RemoveObsoleteFilesAsync(ModManifest? previous, IReadOnlyList<ModFileRecord> current, string gamePath, InstallationJournal journal, BackupDescriptor? backup, CancellationToken cancellationToken)
    {
        if (previous is null) return;
        var currentPaths = current.Select(x => x.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var obsolete in previous.Files.Where(x => !currentPaths.Contains(x.RelativePath)))
        {
            var ownership = await _repository.GetOwnershipAsync(obsolete.RelativePath, cancellationToken).ConfigureAwait(false);
            if (ownership is null || ownership.ModIds.Count != 1 || !ownership.ModIds.Contains(previous.Id)) continue;
            var target = SafePath.CombineUnderRoot(gamePath, obsolete.RelativePath);
            if (!File.Exists(target)) continue;
            var backupPath = backup is not null && backup.RelativeFiles.Contains(obsolete.RelativePath, StringComparer.OrdinalIgnoreCase)
                ? SafePath.CombineUnderRoot(backup.RootPath, obsolete.RelativePath) : null;
            journal.Operations.Add(new JournalOperation { Kind = FileOperationKind.Deleted, TargetRelativePath = obsolete.RelativePath, BackupPath = backupPath });
            await _journals.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
            File.Delete(target);
        }
    }

    private bool Rollback(InstallationJournal journal)
    {
        try
        {
            foreach (var operation in journal.Operations.AsEnumerable().Reverse())
            {
                var target = SafePath.CombineUnderRoot(journal.GamePath, operation.TargetRelativePath);
                if (operation.Kind == FileOperationKind.Created)
                {
                    if (File.Exists(target)) File.Delete(target);
                }
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
            _logger.LogCritical(ex, "Rollback failed for journal {JournalId}; recovery is required", journal.Id);
            return false;
        }
    }

    private async Task RemoveOwnerReferencesAsync(Guid modId, CancellationToken cancellationToken)
    {
        foreach (var owner in await _repository.GetAllOwnershipAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!owner.ModIds.Remove(modId)) continue;
            if (owner.ModIds.Count == 0) await _repository.DeleteOwnershipAsync(owner.RelativePath, cancellationToken).ConfigureAwait(false);
            else await _repository.SaveOwnershipAsync(owner, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RestoreOwnerReferencesAsync(ModManifest manifest, CancellationToken cancellationToken)
    {
        foreach (var file in manifest.Files)
        {
            var owner = await _repository.GetOwnershipAsync(file.RelativePath, cancellationToken).ConfigureAwait(false)
                ?? new FileOwnershipRecord { RelativePath = file.RelativePath };
            owner.ModIds.Add(manifest.Id);
            await _repository.SaveOwnershipAsync(owner, cancellationToken).ConfigureAwait(false);
        }
    }

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
