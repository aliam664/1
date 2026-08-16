using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class ManifestTests
{
    [Fact]
    public async Task Repository_PersistsManifestAndOwnershipAsJson()
    {
        using var environment = new TestEnvironment();
        var id = Guid.NewGuid();
        var manifest = new ModManifest { Id = id, Name = "Test Car", Category = ModCategory.Car, Files = [new ModFileRecord { RelativePath = "content/cars/test/data.acd", Size = 3, Sha256 = "abc" }] };
        var repository = environment.Get<IModRepository>();
        await repository.SaveAsync(manifest);
        await repository.SaveOwnershipAsync(new FileOwnershipRecord { RelativePath = manifest.Files[0].RelativePath, ModIds = [id] });
        var loaded = await repository.GetAsync(id);
        var owner = await repository.GetOwnershipAsync(manifest.Files[0].RelativePath);
        Assert.Equal("Test Car", loaded!.Name);
        Assert.Contains(id, owner!.ModIds);
        Assert.True(File.Exists(Path.Combine(environment.Root, "data", "manifests", id + ".json")));
    }

    [Fact]
    public async Task Verify_ReportsSha256Mismatch()
    {
        using var environment = new TestEnvironment();
        const string relative = "content/cars/test/file.txt";
        var file = Path.Combine(environment.GamePath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "actual");
        var manifest = new ModManifest { Name = "Broken", Files = [new ModFileRecord { RelativePath = relative, Size = 6, Sha256 = new string('0', 64) }] };
        var result = await environment.Get<IManifestService>().VerifyAsync(manifest, environment.GamePath);
        Assert.False(result.IsHealthy);
        Assert.Equal("SHA-256 mismatch", Assert.Single(result.Issues).Reason);
    }
}
