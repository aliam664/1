using System.Text.Json;
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
    private readonly IInstallationLockService _installationLocks;
    private readonly IUserErrorMessageService _userErrors;
    private readonly IModUninstaller _uninstaller;
    private readonly ILogger<TransactionalModInstaller> _logger;

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
        IInstallationLockService installationLocks,
        IUserErrorMessageService userErrors,
        IModUninstaller uninstaller,
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
        _installationLocks = installationLocks;
        _userErrors = userErrors;
        _uninstaller = uninstaller;
        _logger = logger;
    }

    public Task<ModAnalysis> AnalyzeAsync(string archivePath, string gamePath, CancellationToken cancellationToken = default) =>
        AnalyzeCoreAsync(archivePath, gamePath, null, cancellationToken);

    public async Task<ModAnalysis> ApplySkinTargetAsync(ModAnalysis analysis, string carFolderName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var normalizedCar = SafePath.NormalizeRelative(carFolderName).Replace('\\', '/');
        if (normalizedCar.Contains('/')) throw new ModHubException("Enter one Assetto Corsa car folder name, without slashes.");
        if (analysis.Plan.Category != ModCategory.Skin || analysis.Plan.Files.All(x => !x.DestinationPath.Contains("/_select_car_/", StringComparison.OrdinalIgnoreCase)))
            return analysis;
        var files = analysis.Plan.Files.Select(x => x with { DestinationPath = x.DestinationPath.Replace("content/cars/_select_car_/", $"content/cars/{normalizedCar}/", StringComparison.OrdinalIgnoreCase) }).ToArray();
        var plan = new ModInstallPlan
        {
            SuggestedName = analysis.Plan.SuggestedName,
            PackageId = analysis.Plan.PackageId,
            Author = analysis.Plan.Author,
            Version = analysis.Plan.Version,
            Description = analysis.Plan.Description,
            Metadata = new Dictionary<string, string>(analysis.Plan.Metadata, StringComparer.OrdinalIgnoreCase),
            Category = analysis.Plan.Category,
            RootPrefixRemoved = analysis.Plan.RootPrefixRemoved,
            Files = files,
            Warnings = analysis.Plan.Warnings.Where(x => !x.Contains("destination car", StringComparison.OrdinalIgnoreCase)).ToArray()
        };
        var conflicts = (await _conflicts.DetectAsync(analysis.GamePath, files, null, cancellationToken).ConfigureAwait(false))
            .Concat(analysis.Conflicts.Where(x => x.Kind == ConflictKind.InsufficientSpace))
            .ToArray();
        return new ModAnalysis { ArchivePath = analysis.ArchivePath, GamePath = analysis.GamePath, Plan = plan, Conflicts = conflicts, RequiredDiskBytes = analysis.RequiredDiskBytes };
    }

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
        var manifest = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
        if (manifest.Status == ModStatus.Disabled) return;
        var gamePath = RequiredGamePath(manifest);
        await using var installationLock = await _installationLocks.AcquireAsync(gamePath, cancellationToken).ConfigureAwait(false);
        var ownershipByPath = (await _repository.GetAllOwnershipAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => OwnershipKey(x.RelativePath), StringComparer.OrdinalIgnoreCase);
        if (manifest.Files.Any(file => ownershipByPath.TryGetValue(OwnershipKey(file.RelativePath), out var owner) && owner.ModIds.Contains(modId) && !IsTopOwner(owner, modId)))
            throw new ModHubException("This mod cannot be disabled while a newer mod overrides the same files. Disable or uninstall the newer mod first.");
        var moved = new List<(ModFileRecord File, string GameFile, string DisabledFile)>();
        try
        {
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ownershipByPath.TryGetValue(OwnershipKey(file.RelativePath), out var ownership);
                if (!IsTopOwner(ownership, modId)) continue;
                if (file.WasExisting && (string.IsNullOrWhiteSpace(file.BackupPath) || !File.Exists(file.BackupPath)))
                    throw new ModHubException($"Mod cannot be disabled safely because the pre-install backup is missing: {file.RelativePath}");
                var source = SafePath.CombineUnderRoot(gamePath, file.RelativePath);
                if (!File.Exists(source)) continue;
                var disabledRoot = Path.Combine(_paths.DisabledModsDirectory, modId.ToString("N"));
                var destination = SafePath.CombineUnderRoot(disabledRoot, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination, true);
                file.DisabledStorePath = destination;
                moved.Add((file, source, destination));
                var originalBackup = file.BackupPath;
                if (file.WasExisting && !string.IsNullOrWhiteSpace(originalBackup) && File.Exists(originalBackup))
                    await AtomicCopyAsync(originalBackup, source, cancellationToken).ConfigureAwait(false);
            }
            manifest.Status = ModStatus.Disabled;
            manifest.UpdatedAt = DateTimeOffset.UtcNow;
            await _repository.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            foreach (var item in moved.AsEnumerable().Reverse())
            {
                if (File.Exists(item.GameFile)) File.Delete(item.GameFile);
                if (File.Exists(item.DisabledFile)) { Directory.CreateDirectory(Path.GetDirectoryName(item.GameFile)!); File.Move(item.DisabledFile, item.GameFile, true); }
                item.File.DisabledStorePath = null;
            }
            throw;
        }
    }

    public async Task EnableAsync(Guid modId, CancellationToken cancellationToken = default)
    {
        var manifest = await RequiredManifestAsync(modId, cancellationToken).ConfigureAwait(false);
        if (manifest.Status == ModStatus.Enabled) return;
        var gamePath = RequiredGamePath(manifest);
        await using var installationLock = await _installationLocks.AcquireAsync(gamePath, cancellationToken).ConfigureAwait(false);
        var ownershipByPath = (await _repository.GetAllOwnershipAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => OwnershipKey(x.RelativePath), StringComparer.OrdinalIgnoreCase);
        var enabled = new List<(ModFileRecord File, string GameFile, string DisabledFile)>();
        try
        {
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var disabledSource = file.DisabledStorePath;
                if (string.IsNullOrWhiteSpace(disabledSource) || !File.Exists(disabledSource)) continue;
                ownershipByPath.TryGetValue(OwnershipKey(file.RelativePath), out var ownership);
                var target = SafePath.CombineUnderRoot(gamePath, file.RelativePath);
                if (!IsTopOwner(ownership, modId) && File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(disabledSource, target, true);
                enabled.Add((file, target, disabledSource));
                file.DisabledStorePath = null;
            }
            manifest.Status = manifest.Files.Any(x => !string.IsNullOrWhiteSpace(x.DisabledStorePath) && File.Exists(x.DisabledStorePath)) ? ModStatus.Disabled : ModStatus.Enabled;
            manifest.UpdatedAt = DateTimeOffset.UtcNow;
            await _repository.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            foreach (var item in enabled.AsEnumerable().Reverse())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.DisabledFile)!);
                if (File.Exists(item.GameFile)) File.Move(item.GameFile, item.DisabledFile, true);
                var originalBackup = item.File.BackupPath;
                if (item.File.WasExisting && !string.IsNullOrWhiteSpace(originalBackup) && File.Exists(originalBackup))
                    await AtomicCopyAsync(originalBackup, item.GameFile, CancellationToken.None).ConfigureAwait(false);
                item.File.DisabledStorePath = item.DisabledFile;
            }
            throw;
        }
    }

    public Task<UninstallAnalysis> AnalyzeUninstallAsync(Guid modId, CancellationToken cancellationToken = default) =>
        _uninstaller.AnalyzeAsync(modId, cancellationToken);

    public Task UninstallAsync(Guid modId, CancellationToken cancellationToken = default) =>
        _uninstaller.UninstallAsync(modId, new UninstallOptions(), cancellationToken);

    public Task UninstallAsync(Guid modId, UninstallOptions options, CancellationToken cancellationToken = default) =>
        _uninstaller.UninstallAsync(modId, options, cancellationToken);

    private async Task<ModAnalysis> AnalyzeCoreAsync(string archivePath, string gamePath, Guid? currentModId, CancellationToken cancellationToken)
    {
        var fullGamePath = Path.GetFullPath(gamePath);
        if (!Directory.Exists(fullGamePath)) throw new DirectoryNotFoundException("The selected game directory does not exist.");
        var inspection = await _archives.InspectAsync(archivePath, cancellationToken).ConfigureAwait(false);
        var detectedPlan = _structures.Detect(Path.GetFileName(archivePath), inspection.Entries);
        var plan = await ApplyPackageMetadataAsync(detectedPlan, inspection, cancellationToken).ConfigureAwait(false);
        var conflicts = (await _conflicts.DetectAsync(fullGamePath, plan.Files, currentModId, cancellationToken).ConfigureAwait(false)).ToList();
        if (plan.Category == ModCategory.Miscellaneous)
            conflicts.Add(new(".", ConflictKind.UnknownStructure, "No canonical Assetto Corsa root was detected. Review every destination and explicitly confirm this package before installation."));
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

    private async Task<ModInstallPlan> ApplyPackageMetadataAsync(ModInstallPlan plan, ArchiveInspection inspection, CancellationToken cancellationToken)
    {
        var metadataEntry = inspection.Entries.FirstOrDefault(x => !x.IsDirectory && !x.ArchivePath.Contains('/')
            && (x.ArchivePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || x.ArchivePath.Equals("acmodhub.manifest.json", StringComparison.OrdinalIgnoreCase)));
        if (metadataEntry is null) return plan;
        var json = await _archives.ReadTextEntryAsync(inspection.ArchivePath, metadataEntry.ArchivePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return plan;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16 });
            var root = document.RootElement;
            var game = ReadString(root, "game");
            if (!string.IsNullOrWhiteSpace(game) && !game.Equals("assetto-corsa", StringComparison.OrdinalIgnoreCase))
                throw new ModHubException($"This package targets '{game}', not Assetto Corsa.");
            var metadata = new Dictionary<string, string>(plan.Metadata, StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
                if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                    metadata[property.Name] = property.Value.ToString();
            return new ModInstallPlan
            {
                SuggestedName = ReadString(root, "name") ?? plan.SuggestedName,
                PackageId = ReadString(root, "id"),
                Author = ReadString(root, "author") ?? plan.Author,
                Version = ReadString(root, "version") ?? plan.Version,
                Description = ReadString(root, "description"),
                Metadata = metadata,
                Category = plan.Category,
                RootPrefixRemoved = plan.RootPrefixRemoved,
                Files = plan.Files,
                Warnings = plan.Warnings
            };
        }
        catch (JsonException ex)
        {
            throw new ModHubException("The package manifest.json is not valid JSON.", ex);
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        return null;
    }

    private async Task<InstallResult> InstallCoreAsync(ModAnalysis analysis, InstallOptions options, Guid modId, ModManifest? previous, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(options);
        var gamePath = previous is null ? analysis.GamePath : RequiredGamePath(previous);
        var blocking = analysis.Conflicts.Where(x => (x.Kind is ConflictKind.UnsafePath or ConflictKind.LockedFile or ConflictKind.InsufficientSpace) || !options.AllowOverwriteConflicts).ToArray();
        if (blocking.Length > 0)
            return new InstallResult { Success = false, ModId = modId, Error = string.Join(Environment.NewLine, blocking.Take(5).Select(x => x.Message)) };

        await using var installationLock = await _installationLocks.AcquireAsync(gamePath, cancellationToken).ConfigureAwait(false);
        var staging = Path.Combine(_paths.CacheDirectory, "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var previousOwnership = previous is null
            ? new List<FileOwnershipRecord>()
            : (await _repository.GetAllOwnershipAsync(cancellationToken).ConfigureAwait(false)).Where(x => x.ModIds.Contains(modId)).Select(CloneOwnership).ToList();
        var journal = new InstallationJournal { ModId = modId, GamePath = gamePath, ArchivePath = analysis.ArchivePath, Kind = previous is null ? TransactionKind.Install : TransactionKind.Update, PreviousManifest = previous, PreviousOwnership = previousOwnership };
        _logger.LogInformation("{TransactionKind} transaction {TransactionId} started for mod {ModId}, game root {GameRoot}, archive {ArchivePath}, files {FileCount}", journal.Kind, journal.Id, modId, gamePath, analysis.ArchivePath, analysis.Plan.Files.Count);
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
                await _journals.AppendOperationAsync(journal, new JournalOperation { Kind = existed ? FileOperationKind.Replaced : FileOperationKind.Created, TargetRelativePath = planned.DestinationPath, BackupPath = backupPath }, cancellationToken).ConfigureAwait(false);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await AtomicCopyAsync(source, target, cancellationToken).ConfigureAwait(false);
                var hash = await _hashes.ComputeSha256Async(target, cancellationToken).ConfigureAwait(false);
                var previousFile = previous?.Files.FirstOrDefault(x => OwnershipKey(x.RelativePath).Equals(OwnershipKey(planned.DestinationPath), StringComparison.OrdinalIgnoreCase));
                records.Add(new ModFileRecord
                {
                    RelativePath = planned.DestinationPath.Replace('\\', '/'),
                    Size = new FileInfo(target).Length,
                    Sha256 = hash,
                    WasExisting = previousFile?.WasExisting ?? existed,
                    BackupPath = previousFile?.BackupPath ?? backupPath
                });
                Report(progress, InstallStage.Install, 0.15 + 0.65 * (i + 1d) / analysis.Plan.Files.Count, "Installing files", planned.DestinationPath, i + 1, analysis.Plan.Files.Count);
            }

            await RemoveObsoleteFilesAsync(previous, records, gamePath, journal, backup, cancellationToken).ConfigureAwait(false);
            var package = await CachePackageAsync(analysis.ArchivePath, modId, journal.Id, cancellationToken).ConfigureAwait(false);
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
            foreach (var item in analysis.Plan.Metadata) manifest.Metadata[item.Key] = item.Value;
            if (!string.IsNullOrWhiteSpace(analysis.Plan.PackageId)) manifest.Metadata["packageId"] = analysis.Plan.PackageId;
            if (!string.IsNullOrWhiteSpace(analysis.Plan.Description)) manifest.Metadata["description"] = analysis.Plan.Description;
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
            if (previous?.SourceArchivePath is { } oldPackage && !oldPackage.Equals(package, StringComparison.OrdinalIgnoreCase)) TryDeleteCachedPackage(oldPackage);
            _logger.LogInformation("Transaction {TransactionId} committed successfully for mod {ModId}", journal.Id, modId);
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
                await RestoreOwnershipSnapshotAsync(journal.PreviousOwnership, CancellationToken.None).ConfigureAwait(false);
                await _repository.SaveAsync(previous, CancellationToken.None).ConfigureAwait(false);
            }
            else await _repository.DeleteAsync(modId, CancellationToken.None).ConfigureAwait(false);
            journal.State = rolledBack ? JournalState.RolledBack : JournalState.RecoveryRequired;
            journal.Stage = InstallStage.Failed;
            journal.Error = ex.Message;
            await _journals.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
            return new InstallResult { Success = false, ModId = modId, Error = _userErrors.ToUserMessage(ex, "Installation"), WasRolledBack = rolledBack };
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { }
        }
    }

    private async Task<ModManifest> RequiredManifestAsync(Guid modId, CancellationToken cancellationToken) =>
        await _repository.GetAsync(modId, cancellationToken).ConfigureAwait(false) ?? throw new ModHubException("The selected mod is not installed.");

    private static string RequiredGamePath(ModManifest manifest) =>
        manifest.Metadata.TryGetValue("gamePath", out var path) && !string.IsNullOrWhiteSpace(path) ? path : throw new ModHubException("The mod manifest does not contain its game path.");

    private static void Report(IProgress<InstallProgress>? progress, InstallStage stage, double percentage, string message, string? file = null, int current = 0, int total = 0) =>
        progress?.Report(new(stage, percentage, message, file, current, total));

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

    private async Task<string> CachePackageAsync(string archivePath, Guid modId, Guid transactionId, CancellationToken cancellationToken)
    {
        var packages = Path.Combine(_paths.CacheDirectory, "packages");
        Directory.CreateDirectory(packages);
        var destination = Path.Combine(packages, $"{modId:N}-{transactionId:N}{Path.GetExtension(archivePath).ToLowerInvariant()}");
        await AtomicCopyAsync(archivePath, destination, cancellationToken).ConfigureAwait(false);
        return destination;
    }

    private async Task SaveOwnershipAsync(ModManifest manifest, ModManifest? previous, CancellationToken cancellationToken)
    {
        var ownership = (await _repository.GetAllOwnershipAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => OwnershipKey(x.RelativePath), StringComparer.OrdinalIgnoreCase);
        var deletions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (previous is not null)
        {
            var currentPaths = manifest.Files.Select(x => OwnershipKey(x.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var old in previous.Files.Where(x => !currentPaths.Contains(OwnershipKey(x.RelativePath))))
            {
                if (!ownership.TryGetValue(OwnershipKey(old.RelativePath), out var owner)) continue;
                owner.ModIds.Remove(manifest.Id);
                owner.OwnerStack.RemoveAll(x => x == manifest.Id);
                if (owner.ModIds.Count == 0) { ownership.Remove(OwnershipKey(old.RelativePath)); deletions.Add(old.RelativePath); }
            }
        }
        foreach (var file in manifest.Files)
        {
            var key = OwnershipKey(file.RelativePath);
            if (!ownership.TryGetValue(key, out var owner)) { owner = new FileOwnershipRecord { RelativePath = file.RelativePath }; ownership.Add(key, owner); }
            NormalizeOwnerStack(owner);
            owner.ModIds.Add(manifest.Id);
            owner.OwnerStack.RemoveAll(x => x == manifest.Id);
            owner.OwnerStack.Add(manifest.Id);
            owner.UpdatedAt = DateTimeOffset.UtcNow;
            deletions.Remove(file.RelativePath);
        }
        await _repository.ApplyOwnershipChangesAsync(ownership.Values, deletions, cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveObsoleteFilesAsync(ModManifest? previous, IReadOnlyList<ModFileRecord> current, string gamePath, InstallationJournal journal, BackupDescriptor? backup, CancellationToken cancellationToken)
    {
        if (previous is null) return;
        var currentPaths = current.Select(x => x.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ownershipByPath = (await _repository.GetAllOwnershipAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => OwnershipKey(x.RelativePath), StringComparer.OrdinalIgnoreCase);
        foreach (var obsolete in previous.Files.Where(x => !currentPaths.Contains(x.RelativePath)))
        {
            ownershipByPath.TryGetValue(OwnershipKey(obsolete.RelativePath), out var ownership);
            if (!IsTopOwner(ownership, previous.Id)) continue;
            var target = SafePath.CombineUnderRoot(gamePath, obsolete.RelativePath);
            if (!File.Exists(target)) continue;
            var originalBackup = obsolete.BackupPath;
            if (obsolete.WasExisting && (string.IsNullOrWhiteSpace(originalBackup) || !File.Exists(originalBackup)))
                throw new InstallationException($"Update cannot remove '{obsolete.RelativePath}' because its original backup is missing.");
            var rollbackPath = backup is not null && backup.RelativeFiles.Contains(obsolete.RelativePath, StringComparer.OrdinalIgnoreCase)
                ? SafePath.CombineUnderRoot(backup.RootPath, obsolete.RelativePath) : null;
            await _journals.AppendOperationAsync(journal, new JournalOperation { Kind = obsolete.WasExisting ? FileOperationKind.Replaced : FileOperationKind.Deleted, TargetRelativePath = obsolete.RelativePath, BackupPath = rollbackPath }, cancellationToken).ConfigureAwait(false);
            if (obsolete.WasExisting && !string.IsNullOrWhiteSpace(originalBackup) && File.Exists(originalBackup))
                await AtomicCopyAsync(originalBackup, target, cancellationToken).ConfigureAwait(false);
            else
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
        var upserts = new List<FileOwnershipRecord>();
        var deletions = new List<string>();
        foreach (var owner in await _repository.GetAllOwnershipAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!owner.ModIds.Remove(modId)) continue;
            owner.OwnerStack.RemoveAll(x => x == modId);
            if (owner.ModIds.Count == 0) deletions.Add(owner.RelativePath); else upserts.Add(owner);
        }
        await _repository.ApplyOwnershipChangesAsync(upserts, deletions, cancellationToken).ConfigureAwait(false);
    }

    private Task RestoreOwnershipSnapshotAsync(IEnumerable<FileOwnershipRecord> snapshot, CancellationToken cancellationToken) =>
        _repository.ApplyOwnershipChangesAsync(snapshot.Select(CloneOwnership), [], cancellationToken);

    private static FileOwnershipRecord CloneOwnership(FileOwnershipRecord source) => new()
    {
        RelativePath = source.RelativePath,
        ModIds = new HashSet<Guid>(source.ModIds),
        OwnerStack = new List<Guid>(source.OwnerStack),
        UpdatedAt = source.UpdatedAt
    };

    private void TryDeleteCachedPackage(string path)
    {
        try
        {
            var packageRoot = Path.GetFullPath(Path.Combine(_paths.CacheDirectory, "packages")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove cached package {PackagePath}", path);
        }
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

    private static string OwnershipKey(string path) => path.Replace('\\', '/').Trim('/');

}
