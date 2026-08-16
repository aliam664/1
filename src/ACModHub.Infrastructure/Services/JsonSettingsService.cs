using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;

namespace ACModHub.Infrastructure.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _path;
    private readonly AtomicJsonStore _store = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsService(IAppPaths paths) => _path = Path.Combine(paths.DataRoot, "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await _store.ReadAsync(_path, new AppSettings(), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _store.WriteAsync(_path, settings, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
