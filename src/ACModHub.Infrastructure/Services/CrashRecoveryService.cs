using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

public sealed class CrashRecoveryService : ICrashRecoveryService
{
    private readonly IJournalStore _journals;
    private readonly ILogger<CrashRecoveryService> _logger;

    public CrashRecoveryService(IJournalStore journals, ILogger<CrashRecoveryService> logger)
    {
        _journals = journals;
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
}
