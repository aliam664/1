using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class InstallationTests
{
    [Fact]
    public async Task Install_CreatesFilesManifestOwnershipAndHash()
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip("race_car.zip", ("race_car/ui/ui_car.json", "{}"), ("race_car/data.acd", "car-data"));
        var installer = environment.Get<IModInstaller>();
        var analysis = await installer.AnalyzeAsync(archive, environment.GamePath);
        var result = await installer.InstallAsync(analysis, new InstallOptions());
        Assert.True(result.Success, result.Error);
        var manifest = await environment.Get<IModRepository>().GetAsync(result.ModId!.Value);
        Assert.NotNull(manifest);
        Assert.Equal(ModCategory.Car, manifest.Category);
        Assert.All(manifest.Files, x => Assert.Equal(64, x.Sha256.Length));
        Assert.True(File.Exists(Path.Combine(environment.GamePath, "content", "cars", "race_car", "data.acd")));
        Assert.Contains(result.ModId.Value, (await environment.Get<IModRepository>().GetOwnershipAsync("content/cars/race_car/data.acd"))!.ModIds);
    }

    [Fact]
    public async Task Uninstall_RemovesOnlySoleOwnedFiles()
    {
        using var environment = new TestEnvironment();
        var firstArchive = environment.CreateZip("first.zip", ("content/cars/shared/data.acd", "first"));
        var secondArchive = environment.CreateZip("second.zip", ("content/cars/shared/data.acd", "second"));
        var installer = environment.Get<IModInstaller>();
        var first = await installer.InstallAsync(await installer.AnalyzeAsync(firstArchive, environment.GamePath), new InstallOptions());
        var second = await installer.InstallAsync(await installer.AnalyzeAsync(secondArchive, environment.GamePath), new InstallOptions { AllowOverwriteConflicts = true });
        Assert.True(first.Success); Assert.True(second.Success);
        var target = Path.Combine(environment.GamePath, "content", "cars", "shared", "data.acd");
        await installer.UninstallAsync(second.ModId!.Value);
        Assert.True(File.Exists(target));
        Assert.Contains(first.ModId!.Value, (await environment.Get<IModRepository>().GetOwnershipAsync("content/cars/shared/data.acd"))!.ModIds);
        await installer.UninstallAsync(first.ModId.Value);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task DisableAndEnable_MovesSoleOwnedFilesSafely()
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip("toggle.zip", ("content/cars/toggle/data.acd", "data"));
        var installer = environment.Get<IModInstaller>();
        var installed = await installer.InstallAsync(await installer.AnalyzeAsync(archive, environment.GamePath), new InstallOptions());
        var target = Path.Combine(environment.GamePath, "content", "cars", "toggle", "data.acd");
        await installer.DisableAsync(installed.ModId!.Value);
        Assert.False(File.Exists(target));
        await installer.EnableAsync(installed.ModId.Value);
        Assert.True(File.Exists(target));
    }
}
