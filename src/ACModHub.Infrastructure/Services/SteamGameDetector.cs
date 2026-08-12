using System.Security;
using System.Text.RegularExpressions;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using Microsoft.Win32;

namespace ACModHub.Infrastructure.Services;

public sealed partial class RegistrySteamLocationProvider : ISteamLocationProvider
{
    public IEnumerable<string> GetSteamRoots()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view, keyPath, valueName) in Locations())
        {
            string? value = null;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(keyPath);
                value = key?.GetValue(valueName) as string;
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                // A missing or inaccessible registry view is simply not a detection candidate.
            }
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value)) yield return value;
        }
    }

    private static IEnumerable<(RegistryHive Hive, RegistryView View, string Path, string Name)> Locations()
    {
        yield return (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath");
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath");
    }
}

public sealed partial class SteamGameDetector : IGameDetector
{
    public const string AppId = "244210";
    private readonly ISteamLocationProvider _steamLocations;

    public SteamGameDetector(ISteamLocationProvider steamLocations) => _steamLocations = steamLocations;

    public Task<IReadOnlyList<GameInstallation>> DetectAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<GameInstallation>>(() => DetectCore(cancellationToken), cancellationToken);

    public Task<GameInstallation> ValidateManualPathAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var candidate = File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path))! : Path.GetFullPath(path);
        return Task.FromResult(Validate(candidate, "Manual"));
    }

    private IReadOnlyList<GameInstallation> DetectCore(CancellationToken cancellationToken)
    {
        var results = new List<GameInstallation>();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in _steamLocations.GetSteamRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(steamRoot)) continue;
            foreach (var library in ReadLibraries(steamRoot)) roots.Add(library);
        }

        foreach (var library in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
            var gamePath = Path.Combine(library, "steamapps", "common", "assettocorsa");
            if (File.Exists(manifest) || Directory.Exists(gamePath)) results.Add(Validate(gamePath, "Steam"));
        }
        return results.OrderByDescending(x => x.IsValid).ThenBy(x => x.RootPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> ReadLibraries(string steamRoot)
    {
        yield return Path.GetFullPath(steamRoot);
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        string text;
        try { text = File.ReadAllText(vdf); }
        catch (IOException) { yield break; }
        foreach (Match match in LibraryPathRegex().Matches(text))
        {
            var value = match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(value)) yield return Path.GetFullPath(value);
        }
    }

    private static GameInstallation Validate(string root, string source)
    {
        var executable = Path.Combine(root, "acs.exe");
        var content = Path.Combine(root, "content");
        if (!Directory.Exists(root)) return new(root, executable, source, false, "Game directory does not exist.");
        if (!File.Exists(executable)) return new(root, executable, source, false, "acs.exe was not found.");
        if (!Directory.Exists(content)) return new(root, executable, source, false, "The content directory was not found.");
        return new(root, executable, source, true);
    }

    [GeneratedRegex("\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LibraryPathRegex();
}
