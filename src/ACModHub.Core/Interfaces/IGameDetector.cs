using ACModHub.Core.Models;

namespace ACModHub.Core.Interfaces;

public interface IGameDetector
{
    Task<IReadOnlyList<GameInstallation>> DetectAsync(CancellationToken cancellationToken = default);
    Task<GameInstallation> ValidateManualPathAsync(string path, CancellationToken cancellationToken = default);
}

public interface ISteamLocationProvider
{
    IEnumerable<string> GetSteamRoots();
}
