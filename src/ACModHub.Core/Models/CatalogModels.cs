namespace ACModHub.Core.Models;

public sealed class ModCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public string Title { get; init; } = "AC Mod Hub Store";
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<CatalogMod> Mods { get; init; } = [];
}

public sealed class CatalogMod
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Author { get; init; } = "Unknown";
    public string Version { get; init; } = "1.0.0";
    public ModCategory Category { get; init; } = ModCategory.Miscellaneous;
    public string Description { get; init; } = string.Empty;
    public Uri? DownloadUrl { get; init; }
    public string? FileName { get; init; }
    public Uri? ThumbnailUrl { get; init; }
    public string? Sha256 { get; init; }
    public long? ExpectedSize { get; init; }
    public bool Featured { get; init; }
    public List<string> Tags { get; init; } = [];
    public bool IsAvailable => DownloadUrl is not null;
}

public sealed record CatalogLoadResult(ModCatalog Catalog, string Source, string? Warning = null);
