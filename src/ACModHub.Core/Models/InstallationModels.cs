namespace ACModHub.Core.Models;

public sealed record ModConflict(
    string RelativePath,
    ConflictKind Kind,
    string Message,
    IReadOnlyCollection<Guid>? OwnerIds = null);

public sealed class ModAnalysis
{
    public required string ArchivePath { get; init; }
    public required string GamePath { get; init; }
    public required ModInstallPlan Plan { get; init; }
    public required IReadOnlyList<ModConflict> Conflicts { get; init; }
    public long RequiredDiskBytes { get; init; }
    public bool CanInstall => Conflicts.All(x => x.Kind is not ConflictKind.UnsafePath and not ConflictKind.LockedFile and not ConflictKind.InsufficientSpace);
}

public sealed class InstallOptions
{
    public bool AllowOverwriteConflicts { get; init; }
    public bool CreateBackup { get; init; } = true;
    public bool VerifyAfterInstall { get; init; } = true;
    public string? NameOverride { get; init; }
    public string? AuthorOverride { get; init; }
    public string? VersionOverride { get; init; }
}

public sealed record InstallProgress(InstallStage Stage, double Percentage, string Message, string? CurrentFile = null);

public sealed class InstallResult
{
    public required bool Success { get; init; }
    public Guid? ModId { get; init; }
    public string? Error { get; init; }
    public bool WasRolledBack { get; init; }
}

public sealed class JournalOperation
{
    public required FileOperationKind Kind { get; init; }
    public required string TargetRelativePath { get; init; }
    public string? BackupPath { get; init; }
}

public sealed class InstallationJournal
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ModId { get; init; }
    public required string GamePath { get; init; }
    public required string ArchivePath { get; init; }
    public JournalState State { get; set; } = JournalState.InProgress;
    public InstallStage Stage { get; set; } = InstallStage.Analyze;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<JournalOperation> Operations { get; init; } = [];
    public string? Error { get; set; }
}

public sealed class BackupDescriptor
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ModId { get; init; }
    public required string RootPath { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<string> RelativeFiles { get; init; } = [];
}
