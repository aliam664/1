namespace ACModHub.Core.Models;

/// <summary>A bilingual text block in the public catalog schema ({ fa, en }).</summary>
public sealed record LocalizedText(string? Fa, string? En)
{
    public string Get(string language) =>
        language.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? (Fa ?? En ?? string.Empty) : (En ?? Fa ?? string.Empty);
}

/// <summary>Public catalog item status (mirrors the Data catalog schema).</summary>
public enum CatalogModStatus { Draft, Published, Hidden, Deprecated, Revoked }

/// <summary>
/// Root of the public catalog document, matching the aliam664/Data catalog.v1 schema.
/// The embedded fallback in assets/catalog.v1.json uses the same shape.
/// </summary>
public sealed class ModCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public long Revision { get; init; } = 1;
    public DateTimeOffset? GeneratedAt { get; init; }
    public string Repository { get; init; } = "aliam664/Data";
    public string? MinimumLauncherVersion { get; init; }
    public List<CatalogMod> Mods { get; init; } = [];
}

public sealed class CatalogMod
{
    public required string Id { get; init; }
    public CatalogModStatus Status { get; init; } = CatalogModStatus.Published;
    public string? RevocationReason { get; init; }
    public required LocalizedText Name { get; init; }
    public string AuthorName { get; init; } = string.Empty;
    public string? AuthorUrl { get; init; }
    public string Version { get; init; } = "1.0.0";
    public ModCategory Category { get; init; } = ModCategory.Miscellaneous;
    public LocalizedText? Description { get; init; }
    public string? Cover { get; init; }
    /// <summary>Resolved HTTPS cover URL (derived from the catalog document base; null for embedded catalog).</summary>
    public Uri? CoverUri { get; init; }
    public List<string> Tags { get; init; } = [];
    public Uri? DownloadUrl { get; init; }
    public string? FileName { get; init; }
    public string? Sha256 { get; init; }
    public long? ExpectedSize { get; init; }
    public string? MinimumLauncherVersion { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    // Client-computed (not part of the wire schema):
    /// <summary>True when the item may be downloaded and installed (not revoked / not blocked).</summary>
    public bool IsInstallable => Status != CatalogModStatus.Revoked && BlockReason is null;
    /// <summary>Localized reason why the item is not installable (revoked, launcher too old, …).</summary>
    public string? BlockReason { get; init; }
}

public sealed record CatalogLoadResult(
    ModCatalog Catalog,
    string Source,
    bool IsCached = false,
    CatalogWarning Warning = CatalogWarning.None);

public enum CatalogWarning
{
    None,
    /// <summary>Remote endpoints unavailable; showing the last valid cached copy.</summary>
    RemoteUnavailableUsingCache,
    /// <summary>No cache available; showing the embedded empty catalog.</summary>
    RemoteUnavailableUsingEmbedded,
    /// <summary>The catalog requires a newer launcher than the running one.</summary>
    CatalogRequiresNewerLauncher
}
