using ACModHub.Core.Models;

namespace ACModHub.Core.Interfaces;

public interface IDownloadManager
{
    event EventHandler<DownloadProgress>? ProgressChanged;
    IReadOnlyCollection<DownloadJob> Jobs { get; }
    Task<Guid> EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken = default);
    Task PauseAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task ResumeAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task RetryAsync(Guid jobId, CancellationToken cancellationToken = default);
}

public interface IContentProvider
{
    string Name { get; }
    Task<IReadOnlyList<ContentRelease>> CheckUpdatesAsync(IEnumerable<ModManifest> installedMods, CancellationToken cancellationToken = default);
}
