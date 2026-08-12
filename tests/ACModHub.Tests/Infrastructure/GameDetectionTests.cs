using ACModHub.Core.Interfaces;
using ACModHub.Infrastructure.Services;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class GameDetectionTests
{
    [Fact]
    public async Task Detect_FindsAssettoCorsaInSteamLibraryVdf()
    {
        using var environment = new TestEnvironment();
        var steam = Path.Combine(environment.Root, "steam");
        var library = Path.Combine(environment.Root, "library");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        var game = Path.Combine(library, "steamapps", "common", "assettocorsa");
        Directory.CreateDirectory(Path.Combine(game, "content"));
        await File.WriteAllTextAsync(Path.Combine(game, "acs.exe"), "test");
        await File.WriteAllTextAsync(Path.Combine(library, "steamapps", "appmanifest_244210.acf"), "manifest");
        var escaped = library.Replace("\\", "\\\\", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), $"\"libraryfolders\" {{ \"1\" {{ \"path\" \"{escaped}\" }} }}");
        var detector = new SteamGameDetector(new FakeSteamLocation(steam));
        var result = await detector.DetectAsync();
        Assert.Contains(result, x => x.IsValid && x.RootPath == game);
    }

    [Fact]
    public async Task ManualPath_ExplainsMissingExecutable()
    {
        using var environment = new TestEnvironment();
        File.Delete(Path.Combine(environment.GamePath, "acs.exe"));
        var result = await new SteamGameDetector(new FakeSteamLocation()).ValidateManualPathAsync(environment.GamePath);
        Assert.False(result.IsValid);
        Assert.Contains("acs.exe", result.ValidationMessage!);
    }

    private sealed class FakeSteamLocation(params string[] roots) : ISteamLocationProvider
    {
        public IEnumerable<string> GetSteamRoots() => roots;
    }
}
