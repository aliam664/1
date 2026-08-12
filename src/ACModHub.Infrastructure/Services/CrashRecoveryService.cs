using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

public sealed class CrashRecoveryService : ICrashRecoveryService
{
    private readonly IJournalStore _journals;
    private readonly IModRepository _repository;
    private readonly ILogger<CrashRecoveryService> _logger;

    public CrashRecoveryService(IJournalStore journals, IModRepository repository, ILogger<CrashRecoveryService> logger)
    {
        _journals = journals;
        _repository = repository;
        _logger = logger;
    }

    public async Task<int> RecoverAsync(CancellationToken cancellationToken = default)
    {
        var recovered = 0;
        foreach (var journal in await _journals.GetIncompleteAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                foreach (var operation in journal.Operations.AsEnumerable().Reverse())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = SafePath.CombineUnderRoot(journal.GamePath, operation.TargetRelativePath);
                    if (operation.Kind == FileOperationKind.Created)
                    {
                        if (File.Exists(target)) File.Delete(target);
                    }
                    else if ((operation.Kind is FileOperationKind.Replaced or FileOperationKind.Deleted) && !string.IsNullOrWhiteSpace(operation.BackupPath) && File.Exists(operation.BackupPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(operation.BackupPath, target, true);
                    }
                }
                await RestoreMetadataAsync(journal, cancellationToken).ConfigureAwait(false);
                journal.State = JournalState.RolledBack;
                journal.Stage = InstallStage.Failed;
                await _journals.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
                recovered++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                journal.State = JournalState.RecoveryRequired;
                journal.Error = ex.Message;
                await _journals.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
                _logger.LogError(ex, "Could not recover installation journal {JournalId}", journal.Id);
            }
        }
        return recovered;
    }

    private async Task RestoreMetadataAsync(InstallationJournal journal, CancellationToken cancellationToken)
    {
        var ownership = (await _repository.GetAllOwnershipAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(x => Normalize(x.RelativePath), StringComparer.OrdinalIgnoreCase);
        var deletions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var owner in ownership.Values.ToArray())
        {
            if (!owner.ModIds.Remove(journal.ModId)) continue;
            if (owner.ModIds.Count == 0) { ownership.Remove(Normalize(owner.RelativePath)); deletions.Add(owner.RelativePath); }
        }
        if (journal.PreviousManifest is not null)
        {
            foreach (var file in journal.PreviousManifest.Files)
            {
                var key = Normalize(file.RelativePath);
                if (!ownership.TryGetValue(key, out var owner)) { owner = new FileOwnershipRecord { RelativePath = file.RelativePath }; ownership.Add(key, owner); }
                owner.ModIds.Add(journal.ModId);
                deletions.Remove(file.RelativePath);
            }
            await _repository.SaveAsync(journal.PreviousManifest, cancellationToken).ConfigureAwait(false);
        }
        else await _repository.DeleteAsync(journal.ModId, cancellationToken).ConfigureAwait(false);
        await _repository.ApplyOwnershipChangesAsync(ownership.Values, deletions, cancellationToken).ConfigureAwait(false);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');
}
