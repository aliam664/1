using System.Text;
using System.Text.Json;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;

namespace ACModHub.Infrastructure.Services;

public sealed class JsonJournalStore : IJournalStore
{
    private static readonly JsonSerializerOptions OperationJson = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly IAppPaths _paths;
    private readonly AtomicJsonStore _store = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonJournalStore(IAppPaths paths) => _paths = paths;

    public async Task SaveAsync(InstallationJournal journal, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _store.WriteAsync(GetPath(journal.Id), Snapshot(journal), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task AppendOperationAsync(InstallationJournal journal, JournalOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var line = JsonSerializer.Serialize(operation, OperationJson) + "\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            await using var stream = new FileStream(GetOperationsPath(journal.Id), FileMode.Append, FileAccess.Write, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
            journal.Operations.Add(operation);
        }
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
                if (item is null || item.State is not (JournalState.InProgress or JournalState.RecoveryRequired)) continue;
                await LoadOperationsAsync(item, cancellationToken).ConfigureAwait(false);
                result.Add(item);
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid journalId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetPath(journalId); if (File.Exists(path)) File.Delete(path);
            var operations = GetOperationsPath(journalId); if (File.Exists(operations)) File.Delete(operations);
        }
        finally { _gate.Release(); }
    }

    private async Task LoadOperationsAsync(InstallationJournal journal, CancellationToken cancellationToken)
    {
        var path = GetOperationsPath(journal.Id);
        if (!File.Exists(path)) return;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var operation = JsonSerializer.Deserialize<JournalOperation>(line, OperationJson);
                if (operation is not null) journal.Operations.Add(operation);
            }
            catch (JsonException) when (reader.EndOfStream)
            {
                // A torn final append cannot describe an operation that was allowed to modify the game.
                break;
            }
        }
    }

    private static InstallationJournal Snapshot(InstallationJournal source) => new()
    {
        Id = source.Id,
        ModId = source.ModId,
        GamePath = source.GamePath,
        ArchivePath = source.ArchivePath,
        State = source.State,
        Stage = source.Stage,
        StartedAt = source.StartedAt,
        Error = source.Error
    };

    private string GetPath(Guid id) => Path.Combine(_paths.JournalsDirectory, id.ToString("N") + ".json");
    private string GetOperationsPath(Guid id) => Path.Combine(_paths.JournalsDirectory, id.ToString("N") + ".operations.jsonl");
}
