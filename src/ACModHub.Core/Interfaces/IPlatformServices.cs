using ACModHub.Core.Models;

namespace ACModHub.Core.Interfaces;

public interface ILaunchService
{
    Task LaunchAsync(GameInstallation installation, bool throughSteam, CancellationToken cancellationToken = default);
}

public interface IDiagnosticsService
{
    Task<IReadOnlyList<DiagnosticResult>> RunAsync(string? gamePath, CancellationToken cancellationToken = default);
}

public interface IFileHashService
{
    Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default);
}

public interface IDiskSpaceService
{
    long GetAvailableBytes(string path);
}

public interface IFileLockService
{
    bool IsLocked(string path);
}
