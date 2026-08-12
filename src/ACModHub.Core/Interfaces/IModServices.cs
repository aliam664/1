using ACModHub.Core.Models;

namespace ACModHub.Core.Interfaces;

public interface IModScanner
{
    Task<IReadOnlyList<ModManifest>> ScanAsync(string gamePath, bool calculateHashes, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task ImportAsync(IEnumerable<ModManifest> manifests, CancellationToken cancellationToken = default);
}

public interface IModStructureDetector
{
    ModInstallPlan Detect(string archiveName, IReadOnlyList<ArchiveEntryDescriptor> entries);
}

public interface IConflictDetector
{
    Task<IReadOnlyList<ModConflict>> DetectAsync(string gamePath, IReadOnlyList<PlannedFile> files, Guid? currentModId = null, CancellationToken cancellationToken = default);
}

public interface IModRepository
{
    Task<IReadOnlyList<ModManifest>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ModManifest>> QueryAsync(ModQuery query, CancellationToken cancellationToken = default);
    Task<ModManifest?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(ModManifest manifest, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FileOwnershipRecord?> GetOwnershipAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileOwnershipRecord>> GetAllOwnershipAsync(CancellationToken cancellationToken = default);
    Task SaveOwnershipAsync(FileOwnershipRecord ownership, CancellationToken cancellationToken = default);
    Task DeleteOwnershipAsync(string relativePath, CancellationToken cancellationToken = default);
}

public interface IManifestService
{
    Task<VerificationResult> VerifyAsync(ModManifest manifest, string gamePath, CancellationToken cancellationToken = default);
}
