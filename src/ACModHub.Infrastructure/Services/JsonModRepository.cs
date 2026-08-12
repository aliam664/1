using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;

namespace ACModHub.Infrastructure.Services;

public sealed class JsonModRepository : IModRepository
{
    private readonly string _libraryPath;
    private readonly string _ownershipPath;
    private readonly AtomicJsonStore _store = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonModRepository(IAppPaths paths)
    {
        _libraryPath = Path.Combine(paths.DataRoot, "library.json");
        _ownershipPath = Path.Combine(paths.DataRoot, "ownership.json");
    }

    public async Task<IReadOnlyList<ModManifest>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadManifests(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<ModManifest>> QueryAsync(ModQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var source = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        IEnumerable<ModManifest> result = source;
        if (!string.IsNullOrWhiteSpace(query.Search))
            result = result.Where(x => x.Name.Contains(query.Search, StringComparison.CurrentCultureIgnoreCase) || x.Author.Contains(query.Search, StringComparison.CurrentCultureIgnoreCase));
        if (query.Category.HasValue) result = result.Where(x => x.Category == query.Category);
        if (query.Status.HasValue) result = result.Where(x => x.Status == query.Status);
        result = query.Sort switch
        {
            SortMode.Author => result.OrderBy(x => x.Author, StringComparer.CurrentCultureIgnoreCase),
            SortMode.Version => result.OrderBy(x => x.Version, StringComparer.CurrentCultureIgnoreCase),
            SortMode.Size => result.OrderBy(x => x.Size),
            SortMode.InstalledDate => result.OrderBy(x => x.InstalledAt),
            _ => result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        return (query.Descending ? result.Reverse() : result).ToArray();
    }

    public async Task<ModManifest?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id);

    public async Task SaveAsync(ModManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadManifests(cancellationToken).ConfigureAwait(false);
            var index = items.FindIndex(x => x.Id == manifest.Id);
            if (index >= 0) items[index] = manifest; else items.Add(manifest);
            await _store.WriteAsync(_libraryPath, items, cancellationToken).ConfigureAwait(false);
            await _store.WriteAsync(Path.Combine(Path.GetDirectoryName(_libraryPath)!, "manifests", manifest.Id + ".json"), manifest, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadManifests(cancellationToken).ConfigureAwait(false);
            items.RemoveAll(x => x.Id == id);
            await _store.WriteAsync(_libraryPath, items, cancellationToken).ConfigureAwait(false);
            var manifestPath = Path.Combine(Path.GetDirectoryName(_libraryPath)!, "manifests", id + ".json");
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
        }
        finally { _gate.Release(); }
    }

    public async Task<FileOwnershipRecord?> GetOwnershipAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeKey(relativePath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return (await ReadOwnership(cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => NormalizeKey(x.RelativePath) == normalized); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<FileOwnershipRecord>> GetAllOwnershipAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadOwnership(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task SaveOwnershipAsync(FileOwnershipRecord ownership, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadOwnership(cancellationToken).ConfigureAwait(false);
            var key = NormalizeKey(ownership.RelativePath);
            var index = items.FindIndex(x => NormalizeKey(x.RelativePath) == key);
            if (index >= 0) items[index] = ownership; else items.Add(ownership);
            await _store.WriteAsync(_ownershipPath, items, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteOwnershipAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(relativePath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadOwnership(cancellationToken).ConfigureAwait(false);
            items.RemoveAll(x => NormalizeKey(x.RelativePath) == key);
            await _store.WriteAsync(_ownershipPath, items, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private Task<List<ModManifest>> ReadManifests(CancellationToken token) => _store.ReadAsync(_libraryPath, new List<ModManifest>(), token);
    private Task<List<FileOwnershipRecord>> ReadOwnership(CancellationToken token) => _store.ReadAsync(_ownershipPath, new List<FileOwnershipRecord>(), token);
    private static string NormalizeKey(string path) => path.Replace('\\', '/').Trim('/').ToUpperInvariant();
}
