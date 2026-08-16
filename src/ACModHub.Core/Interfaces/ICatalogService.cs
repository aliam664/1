using ACModHub.Core.Models;

namespace ACModHub.Core.Interfaces;

public interface IModCatalogService
{
    Task<CatalogLoadResult> LoadAsync(bool forceRemoteRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads only the last-known-good cached catalog (if present and valid) without any
    /// network access. Used by pages that must never wait on the network (e.g. Dashboard).
    /// </summary>
    Task<CatalogLoadResult?> TryLoadCachedAsync(CancellationToken cancellationToken = default);
}
