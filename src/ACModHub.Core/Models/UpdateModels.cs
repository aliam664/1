namespace ACModHub.Core.Models;

public enum UpdateChannel { Stable, Beta }

public enum UpdateCheckState { Idle, Checking, UpToDate, UpdateAvailable, CheckingFailed }

public enum UpdateDownloadState { Idle, Downloading, Verifying, ReadyToApply, Applying, Failed, Cancelled }

public sealed record UpdateAsset(string Name, long Size, Uri DownloadUrl);

/// <summary>An available launcher update published on GitHub Releases.</summary>
public sealed class UpdateReleaseInfo
{
    public required SemanticVersion Version { get; init; }
    public required string Tag { get; init; }
    public required string Title { get; init; }
    public required string ReleaseNotes { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public required Uri HtmlUrl { get; init; }
    public required IReadOnlyList<UpdateAsset> Assets { get; init; }
    public bool IsPrerelease { get; init; }

    public UpdateAsset? InstallerAsset => Assets.FirstOrDefault(x => x.Name.Equals(AppInfo.InstallerAssetName, StringComparison.OrdinalIgnoreCase));
    public UpdateAsset? ChecksumsAsset => Assets.FirstOrDefault(x => x.Name.Equals(AppInfo.ChecksumsAssetName, StringComparison.OrdinalIgnoreCase));
}

public sealed record UpdateCheckResult(
    bool HasUpdate,
    SemanticVersion? CurrentVersion = null,
    UpdateReleaseInfo? Release = null,
    bool IsPrereleaseUpdate = false);

public sealed record UpdateDownloadProgress(
    UpdateDownloadState State,
    long BytesReceived,
    long? TotalBytes,
    double? Percentage,
    double? SpeedBytesPerSecond,
    TimeSpan? EstimatedTimeRemaining,
    string? Error = null);
