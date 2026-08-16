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
        // Titles and statuses are localized by the UI using the item Key; Message/Detail
        // carry only technical, language-neutral payloads (paths, byte counts).
        var results = new List<DiagnosticResult>();
        var detected = await _gameDetector.DetectAsync(cancellationToken).ConfigureAwait(false);
        results.Add(new("steam", string.Empty, detected.Count > 0 ? DiagnosticStatus.Passed : DiagnosticStatus.Warning,
            string.Empty, detected.Count > 0 ? string.Join("; ", detected.Select(x => x.RootPath)) : null));

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            results.Add(new("gamePath", string.Empty, DiagnosticStatus.Failed, string.Empty, null));
            return results;
        }

        var installation = await _gameDetector.ValidateManualPathAsync(gamePath, cancellationToken).ConfigureAwait(false);
        results.Add(new("gamePath", string.Empty, installation.IsValid ? DiagnosticStatus.Passed : DiagnosticStatus.Failed,
            string.Empty, installation.IsValid ? installation.RootPath : installation.ValidationMessage));
        var content = Path.Combine(installation.RootPath, "content");
        results.Add(new("content", string.Empty, Directory.Exists(content) ? DiagnosticStatus.Passed : DiagnosticStatus.Failed,
            string.Empty, Directory.Exists(content) ? content : null));

        var write = await CheckWriteAccessAsync(installation.RootPath, cancellationToken).ConfigureAwait(false);
        results.Add(new("write", string.Empty, write ? DiagnosticStatus.Passed : DiagnosticStatus.Failed, string.Empty, null));

        try
        {
            var free = _diskSpace.GetAvailableBytes(installation.RootPath);
            results.Add(new("disk", string.Empty, free >= RecommendedFreeBytes ? DiagnosticStatus.Passed : DiagnosticStatus.Warning,
                string.Empty, $"{free / 1024d / 1024d / 1024d:F1} GiB"));
        }
        catch (IOException ex)
        {
            results.Add(new("disk", string.Empty, DiagnosticStatus.Warning, string.Empty, ex.Message));
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
