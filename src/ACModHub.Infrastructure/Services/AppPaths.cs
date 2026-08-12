using ACModHub.Core.Interfaces;

namespace ACModHub.Infrastructure.Services;

public sealed class AppPaths : IAppPaths
{
    public AppPaths(string? dataRoot = null)
    {
        DataRoot = dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ACModHub");
        Directory.CreateDirectory(DataRoot);
        foreach (var path in AllDirectories()) Directory.CreateDirectory(path);
    }

    public string DataRoot { get; }
    public string ManifestsDirectory => Path.Combine(DataRoot, "manifests");
    public string BackupsDirectory => Path.Combine(DataRoot, "backups");
    public string CacheDirectory => Path.Combine(DataRoot, "cache");
    public string DownloadsDirectory => Path.Combine(DataRoot, "downloads");
    public string LogsDirectory => Path.Combine(DataRoot, "logs");
    public string JournalsDirectory => Path.Combine(DataRoot, "journals");
    public string DisabledModsDirectory => Path.Combine(DataRoot, "disabled");

    private IEnumerable<string> AllDirectories()
    {
        yield return ManifestsDirectory;
        yield return BackupsDirectory;
        yield return CacheDirectory;
        yield return DownloadsDirectory;
        yield return LogsDirectory;
        yield return JournalsDirectory;
        yield return DisabledModsDirectory;
    }
}
