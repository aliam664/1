namespace ACModHub.Core.Models;

public sealed class ModFileRecord
{
    public required string RelativePath { get; init; }
    public required long Size { get; init; }
    public required string Sha256 { get; init; }
    public string? DisabledStorePath { get; set; }
}

public sealed class ModManifest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string Author { get; set; } = "Unknown";
    public string Version { get; set; } = "1.0";
    public ModCategory Category { get; set; }
    public ModStatus Status { get; set; } = ModStatus.Installed;
    public long Size { get; set; }
    public string? ThumbnailPath { get; set; }
    public string? SourceArchivePath { get; set; }
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ModFileRecord> Files { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FileOwnershipRecord
{
    public required string RelativePath { get; init; }
    public HashSet<Guid> ModIds { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ModQuery(
    string? Search = null,
    ModCategory? Category = null,
    ModStatus? Status = null,
    SortMode Sort = SortMode.Name,
    bool Descending = false);

public sealed record VerificationIssue(string RelativePath, string Reason, string? ExpectedHash, string? ActualHash);

public sealed class VerificationResult
{
    public required Guid ModId { get; init; }
    public required IReadOnlyList<VerificationIssue> Issues { get; init; }
    public bool IsHealthy => Issues.Count == 0;
}
