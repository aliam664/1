using System.Text.Json;
using ACModHub.Core.Interfaces;
using ACModHub.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ACModHub.Tests.Infrastructure;

public sealed class UpdateApplierTests
{
    private sealed class TestApplier : UpdateApplier
    {
        public TestApplier(AppPaths paths) : base(paths, NullLogger<UpdateApplier>.Instance) { }
        public int LaunchCount { get; private set; }
        public string? LastLaunchedPath { get; private set; }
        public ManualResetEventSlim? BlockUntil { get; set; }
        protected override bool LaunchInstaller(string installerPath)
        {
            LastLaunchedPath = installerPath;
            LaunchCount++;
            BlockUntil?.Wait(TimeSpan.FromSeconds(10));
            return true;
        }
    }

    private static (AppPaths Paths, string UpdatesDir) MakePaths()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "acmodhub-tests", Guid.NewGuid().ToString("N")));
        return (paths, Path.Combine(paths.CacheDirectory, "updates"));
    }

    [Fact]
    public void RefusesPackageOutsideUpdatesDirectory()
    {
        var (paths, _) = MakePaths();
        var applier = new TestApplier(paths);
        var outside = Path.Combine(paths.DataRoot, "AC-Mod-Hub-Setup-v2.0.0.exe");
        File.WriteAllText(outside, "not an installer");
        Assert.False(applier.TryApply(outside));
        Assert.Equal(0, applier.LaunchCount);
    }

    [Fact]
    public void RefusesFileWithoutInstallerNamePrefix()
    {
        var (paths, updatesDir) = MakePaths();
        Directory.CreateDirectory(updatesDir);
        var sneaky = Path.Combine(updatesDir, "evil.exe");
        File.WriteAllText(sneaky, "no");
        var applier = new TestApplier(paths);
        Assert.False(applier.TryApply(sneaky));
        Assert.Equal(0, applier.LaunchCount);
    }

    [Fact]
    public void TryApply_WritesMarkerAndLaunches()
    {
        var (paths, updatesDir) = MakePaths();
        Directory.CreateDirectory(updatesDir);
        var package = Path.Combine(updatesDir, "AC-Mod-Hub-Setup-v2.0.0.exe");
        File.WriteAllText(package, "verified package");
        var applier = new TestApplier(paths);
        Assert.True(applier.TryApply(package));
        Assert.Equal(1, applier.LaunchCount);
        Assert.Equal(package, applier.LastLaunchedPath);
        Assert.True(File.Exists(Path.Combine(paths.DataRoot, "pending-update.json")));
    }

    [Fact]
    public async Task ResolvePendingRestart_ReportsAndCleansMarker()
    {
        var (paths, _) = MakePaths();
        File.WriteAllText(Path.Combine(paths.DataRoot, "pending-update.json"),
            JsonSerializer.Serialize(new { version = "1.0.0", appliedAtUtc = DateTimeOffset.UtcNow }));
        var applier = new TestApplier(paths);
        var outcome = await applier.ResolvePendingRestartAsync();
        Assert.True(outcome.WasApplied);
        Assert.Equal("1.0.0", outcome.AppliedVersion);
        Assert.False(File.Exists(Path.Combine(paths.DataRoot, "pending-update.json")));
    }

    [Fact]
    public async Task NoMarker_ReportsNotApplied()
    {
        var (paths, _) = MakePaths();
        var applier = new TestApplier(paths);
        var outcome = await applier.ResolvePendingRestartAsync();
        Assert.False(outcome.WasApplied);
    }

    [Fact]
    public void ConcurrentApply_IsBlocked()
    {
        var (paths, updatesDir) = MakePaths();
        Directory.CreateDirectory(updatesDir);
        var package = Path.Combine(updatesDir, "AC-Mod-Hub-Setup-v2.0.0.exe");
        File.WriteAllText(package, "verified package");
        var applier = new TestApplier(paths) { BlockUntil = new ManualResetEventSlim() };
        var task = Task.Run(() => applier.TryApply(package));
        SpinWait.SpinUntil(() => applier.IsApplyInProgress, TimeSpan.FromSeconds(5));
        Assert.True(applier.IsApplyInProgress);
        Assert.False(applier.TryApply(package)); // second apply refused while the first is running
        applier.BlockUntil.Set();
        Assert.True(task.Result);
        applier.BlockUntil.Dispose();
    }
}
