using ACModHub.Core.Models;

namespace ACModHub.Core.Interfaces;

/// <summary>
/// Checks the GitHub Releases feed of the launcher repository for a newer version.
/// Implementations must: filter by channel (stable excludes prereleases), reject downgrades,
/// support ETag caching, and never download or install anything.
/// </summary>
public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(UpdateChannel channel, bool forceRefresh, CancellationToken cancellationToken = default);
}

/// <summary>
/// Downloads a verified launcher update package. Verifies SHA-256 against the published
/// SHA256SUMS.txt asset before the package is considered ready to apply.
/// </summary>
public interface IUpdateDownloader
{
    Task<string> DownloadAsync(UpdateReleaseInfo release, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    /// <summary>Best-effort removal of downloaded (already-applied or stale) update packages.</summary>
    void Cleanup(string keepPath);
}

/// <summary>
/// Applies a verified update package. Runs the installer as a detached process after the
/// application has exited, guarded by a single-instance mutex and a restart marker.
/// </summary>
public interface IUpdateApplier
{
    /// <summary>True if another apply operation is already in progress.</summary>
    bool IsApplyInProgress { get; }
    /// <summary>Starts the installer and requests application shutdown. Returns false if spawning failed.</summary>
    bool TryApply(string verifiedPackagePath);
    /// <summary>Called at startup: resolves the pending-restart marker and reports the result.</summary>
    Task<UpdateApplyOutcome> ResolvePendingRestartAsync(CancellationToken cancellationToken = default);
}

public sealed record UpdateApplyOutcome(bool WasApplied, string? AppliedVersion = null, string? Detail = null);
