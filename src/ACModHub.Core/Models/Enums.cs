namespace ACModHub.Core.Models;

public enum ModCategory { Car, Track, Skin, App, Weather, Csp, Graphics, Sound, Miscellaneous }
public enum ModStatus { Installed, Enabled, Disabled, Damaged, Installing, Failed, Unmanaged }
public enum InstallStage { Analyze, Preview, ConflictCheck, Backup, Install, Verify, Done, RollingBack, Failed }
public enum ConflictKind { UntrackedFile, OwnedByAnotherMod, LockedFile, UnsafePath, InsufficientSpace }
public enum JournalState { InProgress, Completed, RolledBack, RecoveryRequired }
public enum TransactionKind { Install, Update, Uninstall }
public enum FileOperationKind { Created, Replaced, Deleted, Moved }
public enum DownloadState { Queued, Downloading, Paused, Verifying, Completed, Failed, Cancelled }
public enum DiagnosticStatus { Passed, Warning, Failed }
public enum SortMode { Name, Author, Version, Size, InstalledDate }
