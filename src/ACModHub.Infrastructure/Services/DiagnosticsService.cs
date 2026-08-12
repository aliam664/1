using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;

namespace ACModHub.Infrastructure.Services;

public sealed class DiagnosticsService : IDiagnosticsService
{
    private const long RecommendedFreeBytes = 5L * 1024 * 1024 * 1024;
    private readonly IGameDetector _gameDetector;
    private readonly IDiskSpaceService _diskSpace;

    public DiagnosticsService(IGameDetector gameDetector, IDiskSpaceService diskSpace)
    {
        _gameDetector = gameDetector;
        _diskSpace = diskSpace;
    }

    public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(string? gamePath, CancellationToken cancellationToken = default)
    {
        var results = new List<DiagnosticResult>();
        var detected = await _gameDetector.DetectAsync(cancellationToken).ConfigureAwait(false);
        results.Add(new("steam", "Steam", detected.Count > 0 ? DiagnosticStatus.Passed : DiagnosticStatus.Warning,
            detected.Count > 0 ? "Steam libraries were inspected successfully." : "No Assetto Corsa Steam installation was detected."));

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            results.Add(new("gamePath", "Game path", DiagnosticStatus.Failed, "Select or detect the Assetto Corsa directory."));
            return results;
        }

        var installation = await _gameDetector.ValidateManualPathAsync(gamePath, cancellationToken).ConfigureAwait(false);
        results.Add(new("gamePath", "Game path", installation.IsValid ? DiagnosticStatus.Passed : DiagnosticStatus.Failed,
            installation.IsValid ? installation.RootPath : installation.ValidationMessage ?? "Invalid path."));
        var content = Path.Combine(installation.RootPath, "content");
        results.Add(new("content", "Content folder", Directory.Exists(content) ? DiagnosticStatus.Passed : DiagnosticStatus.Failed,
            Directory.Exists(content) ? content : "The content folder is missing."));

        var write = await CheckWriteAccessAsync(installation.RootPath, cancellationToken).ConfigureAwait(false);
        results.Add(new("write", "Write access", write ? DiagnosticStatus.Passed : DiagnosticStatus.Failed,
            write ? "The game directory is writable." : "Write access was denied. AC Mod Hub will not request elevation silently."));

        try
        {
            var free = _diskSpace.GetAvailableBytes(installation.RootPath);
            results.Add(new("disk", "Disk space", free >= RecommendedFreeBytes ? DiagnosticStatus.Passed : DiagnosticStatus.Warning,
                $"{free / 1024d / 1024d / 1024d:F1} GiB is available."));
        }
        catch (IOException ex)
        {
            results.Add(new("disk", "Disk space", DiagnosticStatus.Warning, "Available space could not be determined.", ex.Message));
        }
        return results;
    }

    private static async Task<bool> CheckWriteAccessAsync(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path)) return false;
        var probe = Path.Combine(path, $".acmodhub-write-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(probe, "permission test", cancellationToken).ConfigureAwait(false);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return false; }
        finally { if (File.Exists(probe)) File.Delete(probe); }
    }
}
