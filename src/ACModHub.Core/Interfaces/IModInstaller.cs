using ACModHub.Core.Models;

namespace ACModHub.Core.Interfaces;

public interface IModInstaller
{
    Task<ModAnalysis> AnalyzeAsync(string archivePath, string gamePath, CancellationToken cancellationToken = default);
    Task<InstallResult> InstallAsync(ModAnalysis analysis, InstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<InstallResult> UpdateAsync(Guid modId, string archivePath, InstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<InstallResult> ReinstallAsync(Guid modId, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<VerificationResult> RepairAsync(Guid modId, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);
    Task EnableAsync(Guid modId, CancellationToken cancellationToken = default);
    Task DisableAsync(Guid modId, CancellationToken cancellationToken = default);
    Task UninstallAsync(Guid modId, CancellationToken cancellationToken = default);
}
