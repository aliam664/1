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

    /// <summary>Advanced catalog override. Must be absolute HTTPS when set. Empty = use built-in endpoints.</summary>
    public string? CatalogUrl { get; set; }

    public int ConcurrentDownloads { get; set; } = 3;
    public bool LaunchThroughSteam { get; set; } = true;
    public bool VerifyAfterInstall { get; set; } = true;
    public bool KeepPackageCache { get; set; } = true;
    public int BackupRetentionDays { get; set; } = 30;

    /// <summary>Update channel: "stable" (excludes GitHub prereleases) or "beta" (includes prereleases).</summary>
    public string UpdateChannel { get; set; } = "stable";

    /// <summary>Background update check after the main window is shown.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>Disables motion/transitions for accessibility.</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>Update version the user chose "Later" for; not offered again until a newer version appears.</summary>
    public string? SkippedUpdateVersion { get; set; }

    public static AppSettings Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.IsNullOrWhiteSpace(settings.Language))
        {
            settings.Language = settings.Language.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "fa-IR" : "en-US";
        }
        else
        {
            settings.Language = "fa-IR";
        }
        if (settings.UpdateChannel != "beta") settings.UpdateChannel = "stable";
        settings.ConcurrentDownloads = Math.Clamp(settings.ConcurrentDownloads, 1, 8);
        settings.BackupRetentionDays = Math.Clamp(settings.BackupRetentionDays, 1, 365);
        if (string.IsNullOrWhiteSpace(settings.CatalogUrl)) settings.CatalogUrl = null;
        if (!string.IsNullOrWhiteSpace(settings.GamePath) && !Path.IsPathRooted(settings.GamePath.Trim()))
            throw new ArgumentException("The game path must be an absolute path.");
        if (!string.IsNullOrWhiteSpace(settings.GamePath)) settings.GamePath = Path.GetFullPath(settings.GamePath.Trim());
        return settings;
    }
}

public sealed record DiagnosticResult(
    string Key,
    string Title,
    DiagnosticStatus Status,
    string Message,
    string? Detail = null);
