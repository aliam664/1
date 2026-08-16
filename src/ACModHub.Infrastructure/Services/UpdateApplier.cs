using System.Diagnostics;
using System.Text.Json;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Services;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

/// <summary>
/// Applies a verified launcher update by starting the per-user Inno Setup installer as a
/// detached process, after writing a restart marker that the installer honors to relaunch
/// the app. The application exits immediately after spawning. A lock file guards against
/// concurrent apply operations; startup recovery resolves the marker into a health record.
/// </summary>
public sealed class UpdateApplier : IUpdateApplier
{
    private const string PendingMarkerFileName = "pending-update.json";
    private const string HealthFileName = "update-health.json";

    private readonly IAppPaths _paths;
    private readonly ILogger<UpdateApplier> _logger;
    private readonly string _lockPath;

    public UpdateApplier(IAppPaths paths, ILogger<UpdateApplier> logger)
    {
        _paths = paths;
        _logger = logger;
        _lockPath = Path.Combine(paths.CacheDirectory, "updates", "apply.lock");
    }

    public bool IsApplyInProgress
    {
        get
        {
            try
            {
                return File.Exists(_lockPath);
            }
            catch (IOException)
            {
                return true; // fail safe: assume busy when the lock cannot be read
            }
        }
    }

    public bool TryApply(string verifiedPackagePath)
    {
        if (IsApplyInProgress) return false;

        // Never execute anything outside the dedicated, verified updates directory.
        var updatesRoot = Path.GetFullPath(Path.Combine(_paths.CacheDirectory, "updates"));
        var fullPath = Path.GetFullPath(verifiedPackagePath);
        if (!fullPath.StartsWith(updatesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullPath).StartsWith(AppInfo.InstallerAssetName[..^4], StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullPath))
        {
            _logger.LogError("Refusing to apply an update from an untrusted location: {Path}", verifiedPackagePath);
            return false;
        }

        try
        {
            Directory.CreateDirectory(updatesRoot);
            using (var lockHandle = new FileStream(_lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                lockHandle.WriteByte(1);
                lockHandle.Flush(true);
                File.WriteAllText(Path.Combine(_paths.DataRoot, PendingMarkerFileName), JsonSerializer.Serialize(new
                {
                    version = AppInfo.Version,
                    appliedAtUtc = DateTimeOffset.UtcNow
                }));
                if (!LaunchInstaller(fullPath))
                {
                    File.Delete(_lockPath);
                    File.Delete(Path.Combine(_paths.DataRoot, PendingMarkerFileName));
                    return false;
                }
                return true;
            }
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Could not start the update installer");
            return false;
        }
    }

    /// <summary>Seam for tests: spawns the installer process. Must be detached from this process.</summary>
    protected virtual bool LaunchInstaller(string installerPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath)!,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
            };
            return Process.Start(startInfo) is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Failed to start the update installer {Path}", installerPath);
            return false;
        }
    }

    public async Task<UpdateApplyOutcome> ResolvePendingRestartAsync(CancellationToken cancellationToken = default)
    {
        var markerPath = Path.Combine(_paths.DataRoot, PendingMarkerFileName);
        if (!File.Exists(markerPath)) return new UpdateApplyOutcome(false);
        try
        {
            var payload = await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
            var record = JsonSerializer.Deserialize<PendingUpdateMarker>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            File.Delete(markerPath);
            try { File.Delete(_lockPath); } catch (IOException) { }

            var healthPath = Path.Combine(_paths.CacheDirectory, "updates", HealthFileName);
            await File.WriteAllTextAsync(healthPath, JsonSerializer.Serialize(new
            {
                previousVersion = record?.Version,
                resolvedAtUtc = DateTimeOffset.UtcNow
            }), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Update apply resolved at startup (previous version {Version})", record?.Version);
            return new UpdateApplyOutcome(true, record?.Version, "The launcher was updated.");
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Pending update marker could not be resolved");
            return new UpdateApplyOutcome(false);
        }
    }

    private sealed class PendingUpdateMarker
    {
        public string? Version { get; set; }
        public DateTimeOffset? AppliedAtUtc { get; set; }
    }
}
