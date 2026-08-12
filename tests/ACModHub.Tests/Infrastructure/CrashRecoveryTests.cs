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
        var journals = environment.Get<IJournalStore>();
        await journals.SaveAsync(journal);
        await journals.AppendOperationAsync(journal, new JournalOperation { Kind = FileOperationKind.Created, TargetRelativePath = relative });
        var count = await environment.Get<ICrashRecoveryService>().RecoverAsync();
        Assert.Equal(1, count);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task Recovery_RestoresManifestAndOwnershipForInterruptedUninstall()
    {
        using var environment = new TestEnvironment();
        var id = Guid.NewGuid();
        const string relative = "content/cars/recovery/data.acd";
        var manifest = new ModManifest { Id = id, Name = "Recovery", Files = [new ModFileRecord { RelativePath = relative, Size = 1, Sha256 = string.Empty }] };
        manifest.Metadata["gamePath"] = environment.GamePath;
        var repository = environment.Get<IModRepository>();
        await repository.SaveAsync(manifest);
        await repository.SaveOwnershipAsync(new FileOwnershipRecord { RelativePath = relative, ModIds = [id] });
        var journal = new InstallationJournal { ModId = id, GamePath = environment.GamePath, ArchivePath = "uninstall", Kind = TransactionKind.Uninstall, PreviousManifest = manifest };
        await environment.Get<IJournalStore>().SaveAsync(journal);
        await repository.DeleteOwnershipAsync(relative);
        await repository.DeleteAsync(id);

        await environment.Get<ICrashRecoveryService>().RecoverAsync();

        Assert.NotNull(await repository.GetAsync(id));
        Assert.Contains(id, (await repository.GetOwnershipAsync(relative))!.ModIds);
    }
}
