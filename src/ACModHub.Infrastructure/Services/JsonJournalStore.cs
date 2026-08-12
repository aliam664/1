using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;

namespace ACModHub.Infrastructure.Services;

public sealed class JsonJournalStore : IJournalStore
{
    private readonly IAppPaths _paths;
    private readonly AtomicJsonStore _store = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonJournalStore(IAppPaths paths) => _paths = paths;

    public async Task SaveAsync(InstallationJournal journal, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _store.WriteAsync(GetPath(journal.Id), journal, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<InstallationJournal>> GetIncompleteAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = new List<InstallationJournal>();
            foreach (var file in Directory.EnumerateFiles(_paths.JournalsDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var item = await _store.ReadAsync<InstallationJournal?>(file, null, cancellationToken).ConfigureAwait(false);
                if (item is not null && item.State is JournalState.InProgress or JournalState.RecoveryRequired) result.Add(item);
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid journalId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { var path = GetPath(journalId); if (File.Exists(path)) File.Delete(path); }
        finally { _gate.Release(); }
    }

    private string GetPath(Guid id) => Path.Combine(_paths.JournalsDirectory, id.ToString("N") + ".json");
}
