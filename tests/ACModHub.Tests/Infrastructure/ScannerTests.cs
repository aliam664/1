using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class ScannerTests
{
    [Fact]
    public async Task ScanAndImport_CreatesSeparateCarAndSkinOwnership()
    {
        using var environment = new TestEnvironment();
        var carFile = Path.Combine(environment.GamePath, "content", "cars", "scan_car", "data.acd");
        var skinFile = Path.Combine(environment.GamePath, "content", "cars", "scan_car", "skins", "red", "livery.png");
        Directory.CreateDirectory(Path.GetDirectoryName(carFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(skinFile)!);
        await File.WriteAllTextAsync(carFile, "car");
        await File.WriteAllTextAsync(skinFile, "skin");
        var scanner = environment.Get<IModScanner>();
        var found = await scanner.ScanAsync(environment.GamePath, true);
        Assert.Contains(found, x => x.Category == ModCategory.Car);
        Assert.Contains(found, x => x.Category == ModCategory.Skin);
        await scanner.ImportAsync(found);
        var owner = await environment.Get<IModRepository>().GetOwnershipAsync("content/cars/scan_car/skins/red/livery.png");
        Assert.Single(owner!.ModIds);
    }
}
