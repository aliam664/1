using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class ConflictDetectionTests
{
    [Fact]
    public async Task Detect_DistinguishesUntrackedAndOwnedFiles()
    {
        using var environment = new TestEnvironment();
        var untracked = "content/cars/a/data.acd";
        var owned = "content/cars/b/data.acd";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(environment.GamePath, untracked))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(environment.GamePath, owned))!);
        await File.WriteAllTextAsync(Path.Combine(environment.GamePath, untracked), "a");
        await File.WriteAllTextAsync(Path.Combine(environment.GamePath, owned), "b");
        var ownerId = Guid.NewGuid();
        await environment.Get<IModRepository>().SaveOwnershipAsync(new FileOwnershipRecord { RelativePath = owned, ModIds = [ownerId] });
        PlannedFile[] planned = [new(untracked, untracked, 1), new(owned, owned, 1)];
        var conflicts = await environment.Get<IConflictDetector>().DetectAsync(environment.GamePath, planned);
        Assert.Contains(conflicts, x => x.RelativePath == untracked && x.Kind == ConflictKind.UntrackedFile);
        Assert.Contains(conflicts, x => x.RelativePath == owned && x.Kind == ConflictKind.OwnedByAnotherMod && x.OwnerIds!.Contains(ownerId));
    }
}
