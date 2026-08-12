using ACModHub.Core.Models;

namespace ACModHub.Core.Interfaces;

public interface IModCatalogService
{
    Task<CatalogLoadResult> LoadAsync(bool forceRemoteRefresh = false, CancellationToken cancellationToken = default);
}
