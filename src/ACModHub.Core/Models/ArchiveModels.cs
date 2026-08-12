namespace ACModHub.Core.Models;

public sealed record ArchiveEntryDescriptor(
    string ArchivePath,
    long UncompressedSize,
    long CompressedSize,
    bool IsDirectory,
    bool IsEncrypted = false,
    bool IsSymbolicLink = false);

public sealed record PlannedFile(
    string ArchivePath,
    string DestinationPath,
    long Size,
    string? Sha256 = null);

public sealed class ModInstallPlan
{
    public required string SuggestedName { get; init; }
    public string Author { get; init; } = "Unknown";
    public string Version { get; init; } = "1.0";
    public required ModCategory Category { get; init; }
    public required string RootPrefixRemoved { get; init; }
    public required IReadOnlyList<PlannedFile> Files { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class ArchiveInspection
{
    public required string ArchivePath { get; init; }
    public required IReadOnlyList<ArchiveEntryDescriptor> Entries { get; init; }
    public long TotalUncompressedSize => Entries.Where(x => !x.IsDirectory).Sum(x => x.UncompressedSize);
}
