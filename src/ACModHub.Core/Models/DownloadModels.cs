namespace ACModHub.Core.Models;

public sealed class DownloadRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Uri Source { get; init; }
    public required string FileName { get; init; }
    public string? ExpectedSha256 { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DownloadJob
{
    public required DownloadRequest Request { get; init; }
    public DownloadState State { get; set; } = DownloadState.Queued;
    public long BytesReceived { get; set; }
    public long? TotalBytes { get; set; }
    public int RetryCount { get; set; }
    public string? DestinationPath { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DownloadProgress(Guid JobId, DownloadState State, long BytesReceived, long? TotalBytes, double? Percentage);

public sealed record ContentRelease(string Id, string Name, string Version, Uri DownloadUrl, string? Sha256, IReadOnlyDictionary<string, string> Metadata);
