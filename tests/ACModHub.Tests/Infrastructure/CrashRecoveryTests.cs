using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class CrashRecoveryTests
{
    [Fact]
    public async Task Recovery_ReversesIncompleteJournal()
    {
        using var environment = new TestEnvironment();
        const string relative = "content/cars/interrupted/file.txt";
        var target = Path.Combine(environment.GamePath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "partial");
        var journal = new InstallationJournal { ModId = Guid.NewGuid(), GamePath = environment.GamePath, ArchivePath = "test.zip", Stage = InstallStage.Install };
        journal.Operations.Add(new JournalOperation { Kind = FileOperationKind.Created, TargetRelativePath = relative });
        await environment.Get<IJournalStore>().SaveAsync(journal);
        var count = await environment.Get<ICrashRecoveryService>().RecoverAsync();
        Assert.Equal(1, count);
        Assert.False(File.Exists(target));
    }
}
