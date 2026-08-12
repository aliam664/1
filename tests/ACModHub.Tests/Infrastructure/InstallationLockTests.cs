using ACModHub.Core.Interfaces;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class InstallationLockTests
{
    [Fact]
    public async Task Lock_BlocksSameGameRootButAllowsDifferentRoots()
    {
        using var environment = new TestEnvironment();
        var service = environment.Get<IInstallationLockService>();
        await using var first = await service.AcquireAsync(environment.GamePath);
        var otherGame = Path.Combine(environment.Root, "other-game");
        Directory.CreateDirectory(otherGame);
        await using var differentRoot = await service.AcquireAsync(otherGame);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using var blocked = await service.AcquireAsync(environment.GamePath, timeout.Token);
        });
    }
}
