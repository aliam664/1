using ACModHub.Core.Interfaces;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class BackupTests
{
    [Fact]
    public async Task Backup_CreatesAndRestoresOriginalFile()
    {
        using var environment = new TestEnvironment();
        const string relative = "content/cars/test/data.acd";
        var file = Path.Combine(environment.GamePath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "original");
        var service = environment.Get<IBackupService>();
        var backup = await service.CreateAsync(Guid.NewGuid(), environment.GamePath, [relative]);
        await File.WriteAllTextAsync(file, "modified");
        await service.RestoreAsync(backup, environment.GamePath);
        Assert.Equal("original", await File.ReadAllTextAsync(file));
        Assert.Contains(relative.Replace('\\', '/'), backup.RelativeFiles);
    }
}
