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
        Assert.True(result.Success, result.Error ?? "Installation failed.");
        var manifest = await environment.Get<IModRepository>().GetAsync(result.ModId!.Value);
        Assert.NotNull(manifest);
        Assert.Equal(ModCategory.Car, manifest.Category);
        Assert.All(manifest.Files, x => Assert.Equal(64, x.Sha256.Length));
        Assert.True(File.Exists(Path.Combine(environment.GamePath, "content", "cars", "race_car", "data.acd")));
        Assert.Contains(result.ModId.Value, (await environment.Get<IModRepository>().GetOwnershipAsync("content/cars/race_car/data.acd"))!.ModIds);
    }

    [Theory]
    [InlineData("content/tracks/test_track/models.ini", ModCategory.Track)]
    [InlineData("extension/config/cars/test.ini", ModCategory.Csp)]
    public async Task Install_SupportsTrackAndCspPackages(string relativePath, ModCategory expectedCategory)
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip(expectedCategory + ".zip", (relativePath, "data"));
        var installer = environment.Get<IModInstaller>();
        var analysis = await installer.AnalyzeAsync(archive, environment.GamePath);
        Assert.Equal(expectedCategory, analysis.Plan.Category);
        var result = await installer.InstallAsync(analysis, new InstallOptions());
        Assert.True(result.Success, result.Error ?? "Installation failed");
        Assert.True(File.Exists(Path.Combine(environment.GamePath, relativePath)));
    }

    [Fact]
    public async Task Install_PreservesAllRootsInMixedPackage()
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip("mixed-install.zip", ("content/cars/mixed/data.acd", "car"), ("extension/config/cars/mixed.ini", "config"));
        var installer = environment.Get<IModInstaller>();
        var analysis = await installer.AnalyzeAsync(archive, environment.GamePath);
        Assert.Equal(ModCategory.Mixed, analysis.Plan.Category);
        var result = await installer.InstallAsync(analysis, new InstallOptions());
        Assert.True(result.Success, result.Error ?? "Installation failed");
        Assert.True(File.Exists(Path.Combine(environment.GamePath, "content", "cars", "mixed", "data.acd")));
        Assert.True(File.Exists(Path.Combine(environment.GamePath, "extension", "config", "cars", "mixed.ini")));
    }

    [Fact]
    public async Task Analyze_RequiresExplicitConfirmationForUnknownStructure()
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip("unknown.zip", ("mystery/readme.dat", "data"));
        var installer = environment.Get<IModInstaller>();
        var analysis = await installer.AnalyzeAsync(archive, environment.GamePath);
        Assert.Equal(ModCategory.Miscellaneous, analysis.Plan.Category);
        Assert.Contains(analysis.Conflicts, x => x.Kind == ConflictKind.UnknownStructure);
        var result = await installer.InstallAsync(analysis, new InstallOptions());
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Analyze_ReadsOptionalPackageMetadataWithoutInstallingManifestIntoGame()
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip("metadata.zip",
            ("manifest.json", "{\"id\":\"example-car\",\"name\":\"Example Car\",\"version\":\"2.1.0\",\"author\":\"Tester\",\"game\":\"assetto-corsa\"}"),
            ("content/cars/example/data.acd", "data"));
        var analysis = await environment.Get<IModInstaller>().AnalyzeAsync(archive, environment.GamePath);
        Assert.Equal("Example Car", analysis.Plan.SuggestedName);
        Assert.Equal("2.1.0", analysis.Plan.Version);
        Assert.DoesNotContain(analysis.Plan.Files, x => x.DestinationPath.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));
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
        Assert.Equal("first", await File.ReadAllTextAsync(target));
        Assert.Contains(first.ModId!.Value, (await environment.Get<IModRepository>().GetOwnershipAsync("content/cars/shared/data.acd"))!.ModIds);
        await installer.UninstallAsync(first.ModId.Value);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task Uninstall_RestoresFileThatExistedBeforeInstallation()
    {
        using var environment = new TestEnvironment();
        var target = Path.Combine(environment.GamePath, "content", "cars", "existing", "data.acd");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "original-game-file");
        var archive = environment.CreateZip("existing.zip", ("content/cars/existing/data.acd", "modded-file"));
        var installer = environment.Get<IModInstaller>();
        var installed = await installer.InstallAsync(await installer.AnalyzeAsync(archive, environment.GamePath), new InstallOptions { AllowOverwriteConflicts = true });
        Assert.Equal("modded-file", await File.ReadAllTextAsync(target));
        var manifest = await environment.Get<IModRepository>().GetAsync(installed.ModId!.Value);
        var installedFile = Assert.Single(manifest!.Files);
        Assert.True(installedFile.WasExisting);
        Assert.True(File.Exists(installedFile.BackupPath));
        await installer.UninstallAsync(installed.ModId!.Value);
        Assert.Equal("original-game-file", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Update_PreservesOriginalBaselineForLaterUninstall()
    {
        using var environment = new TestEnvironment();
        var target = Path.Combine(environment.GamePath, "content", "cars", "updated", "data.acd");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "original");
        var installer = environment.Get<IModInstaller>();
        var first = await installer.InstallAsync(await installer.AnalyzeAsync(environment.CreateZip("update-v1.zip", ("content/cars/updated/data.acd", "v1")), environment.GamePath), new InstallOptions { AllowOverwriteConflicts = true });
        var updated = await installer.UpdateAsync(first.ModId!.Value, environment.CreateZip("update-v2.zip", ("content/cars/updated/data.acd", "v2")), new InstallOptions { AllowOverwriteConflicts = true });
        Assert.True(updated.Success);
        await installer.UninstallAsync(first.ModId.Value);
        Assert.Equal("original", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Uninstall_RequiresDecisionAndCanPreserveModifiedFile()
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip("modified.zip", ("content/cars/modified/data.acd", "installed"));
        var installer = environment.Get<IModInstaller>();
        var installed = await installer.InstallAsync(await installer.AnalyzeAsync(archive, environment.GamePath), new InstallOptions());
        var target = Path.Combine(environment.GamePath, "content", "cars", "modified", "data.acd");
        await File.WriteAllTextAsync(target, "user-change");
        var analysis = await installer.AnalyzeUninstallAsync(installed.ModId!.Value);
        Assert.Single(analysis.ModifiedFiles);
        await Assert.ThrowsAsync<ACModHub.Core.ModHubException>(() => installer.UninstallAsync(installed.ModId.Value));
        await installer.UninstallAsync(installed.ModId.Value, new UninstallOptions { ModifiedFileAction = ModifiedFileAction.Preserve });
        Assert.Equal("user-change", await File.ReadAllTextAsync(target));
        Assert.Null(await environment.Get<IModRepository>().GetAsync(installed.ModId.Value));
    }

    [Fact]
    public async Task Uninstall_CanDiscardModificationAndRestoreOriginalFile()
    {
        using var environment = new TestEnvironment();
        var target = Path.Combine(environment.GamePath, "content", "cars", "discard", "data.acd");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "original");
        var installer = environment.Get<IModInstaller>();
        var installed = await installer.InstallAsync(await installer.AnalyzeAsync(environment.CreateZip("discard.zip", ("content/cars/discard/data.acd", "installed")), environment.GamePath), new InstallOptions { AllowOverwriteConflicts = true });
        await File.WriteAllTextAsync(target, "user-change");
        await installer.UninstallAsync(installed.ModId!.Value, new UninstallOptions { ModifiedFileAction = ModifiedFileAction.RestoreOrDelete });
        Assert.Equal("original", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Uninstall_BlocksOlderOwnerUntilNewerOverrideIsRemoved()
    {
        using var environment = new TestEnvironment();
        var installer = environment.Get<IModInstaller>();
        var first = await installer.InstallAsync(await installer.AnalyzeAsync(environment.CreateZip("owner-a.zip", ("content/cars/stack/data.acd", "a")), environment.GamePath), new InstallOptions());
        var second = await installer.InstallAsync(await installer.AnalyzeAsync(environment.CreateZip("owner-b.zip", ("content/cars/stack/data.acd", "b")), environment.GamePath), new InstallOptions { AllowOverwriteConflicts = true });
        var analysis = await installer.AnalyzeUninstallAsync(first.ModId!.Value);
        Assert.False(analysis.CanUninstall);
        Assert.Contains(second.ModId!.Value, analysis.BlockingNewerModIds);
    }

    [Fact]
    public async Task Repair_RestoresDamagedFileFromCachedPackage()
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip("repair.zip", ("content/cars/repair/data.acd", "healthy"));
        var installer = environment.Get<IModInstaller>();
        var installed = await installer.InstallAsync(await installer.AnalyzeAsync(archive, environment.GamePath), new InstallOptions());
        var target = Path.Combine(environment.GamePath, "content", "cars", "repair", "data.acd");
        await File.WriteAllTextAsync(target, "damaged");
        var repaired = await installer.RepairAsync(installed.ModId!.Value);
        Assert.True(repaired.IsHealthy);
        Assert.Equal("healthy", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Disable_RestoresPreExistingFileAndEnableReappliesMod()
    {
        using var environment = new TestEnvironment();
        var target = Path.Combine(environment.GamePath, "content", "cars", "toggle-existing", "data.acd");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "original");
        var installer = environment.Get<IModInstaller>();
        var installed = await installer.InstallAsync(await installer.AnalyzeAsync(environment.CreateZip("toggle-existing.zip", ("content/cars/toggle-existing/data.acd", "mod")), environment.GamePath), new InstallOptions { AllowOverwriteConflicts = true });
        await installer.DisableAsync(installed.ModId!.Value);
        Assert.Equal("original", await File.ReadAllTextAsync(target));
        await installer.EnableAsync(installed.ModId.Value);
        Assert.Equal("mod", await File.ReadAllTextAsync(target));
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
