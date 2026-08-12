namespace ACModHub.Core.Models;

public sealed record GameInstallation(
    string RootPath,
    string ExecutablePath,
    string Source,
    bool IsValid,
    string? ValidationMessage = null);

public sealed class AppSettings
{
    public string? GamePath { get; set; }
    public List<string> KnownGamePaths { get; set; } = [];
    public string Language { get; set; } = "fa-IR";
    public string? DownloadDirectory { get; set; }
    public int ConcurrentDownloads { get; set; } = 3;
    public bool LaunchThroughSteam { get; set; } = true;
    public bool VerifyAfterInstall { get; set; } = true;
    public bool KeepPackageCache { get; set; } = true;
    public int BackupRetentionDays { get; set; } = 30;
}

public sealed record DiagnosticResult(
    string Key,
    string Title,
    DiagnosticStatus Status,
    string Message,
    string? Detail = null);
