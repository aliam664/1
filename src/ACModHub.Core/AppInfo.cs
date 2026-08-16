using System.Reflection;

namespace ACModHub.Core;

/// <summary>
/// Runtime identity of the launcher. The version comes from the single source of truth
/// (Directory.Build.props) via assembly informational version — never hard-coded here.
/// </summary>
public static class AppInfo
{
    public const string ProductName = "AC Mod Hub";
    public const string InstallerAssetName = "AC-Mod-Hub-Setup.exe";
    public const string ChecksumsAssetName = "SHA256SUMS.txt";

    private static readonly Lazy<string> LazyVersion = new(() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0");

    /// <summary>Informational version without commit hash (e.g. "2.0.0-preview.1").</summary>
    public static string Version => LazyVersion.Value;

    /// <summary>User-Agent sent by all HTTP clients. Format: ACModHub/{version} (+https://github.com/aliam664/1).</summary>
    public static string UserAgent => $"ACModHub/{Version} (+https://github.com/aliam664/1)";
}
