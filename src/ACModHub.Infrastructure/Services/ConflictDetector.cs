using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;

namespace ACModHub.Infrastructure.Services;

public sealed class ConflictDetector : IConflictDetector
{
    private readonly IModRepository _repository;
    private readonly IFileLockService _locks;

    public ConflictDetector(IModRepository repository, IFileLockService locks)
    {
        _repository = repository;
        _locks = locks;
    }

    public async Task<IReadOnlyList<ModConflict>> DetectAsync(string gamePath, IReadOnlyList<PlannedFile> files, Guid? currentModId = null, CancellationToken cancellationToken = default)
    {
        var result = new List<ModConflict>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target;
            try { target = SafePath.CombineUnderRoot(gamePath, file.DestinationPath); }
            catch (UnsafeArchiveException ex)
            {
                result.Add(new(file.DestinationPath, ConflictKind.UnsafePath, ex.Message));
                continue;
            }
            if (!File.Exists(target)) continue;
            if (_locks.IsLocked(target))
            {
                result.Add(new(file.DestinationPath, ConflictKind.LockedFile, "The destination file is locked by another process."));
                continue;
            }

            var ownership = await _repository.GetOwnershipAsync(file.DestinationPath, cancellationToken).ConfigureAwait(false);
            if (ownership is null || ownership.ModIds.Count == 0)
                result.Add(new(file.DestinationPath, ConflictKind.UntrackedFile, "An untracked game file already exists."));
            else
            {
                var otherOwners = ownership.ModIds.Where(x => x != currentModId).ToArray();
                if (otherOwners.Length > 0)
                    result.Add(new(file.DestinationPath, ConflictKind.OwnedByAnotherMod, "The file is owned by another installed mod.", otherOwners));
            }
        }
        return result;
    }
}
