using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ACModHub.Tests.Infrastructure;

public sealed class RollbackTests
{
    [Fact]
    public async Task InstallFailure_RestoresReplacedFileAndRemovesNewFiles()
    {
        var fake = new IncompleteArchiveService();
        using var environment = new TestEnvironment(services => services.AddSingleton<IArchiveService>(fake));
        const string first = "content/cars/rollback/one.txt";
        var existing = Path.Combine(environment.GamePath, first);
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        await File.WriteAllTextAsync(existing, "original");
        fake.ArchivePath = Path.Combine(environment.Root, "fault.zip");
        await File.WriteAllTextAsync(fake.ArchivePath, "not read by fake");
        var installer = environment.Get<IModInstaller>();
        var analysis = await installer.AnalyzeAsync(fake.ArchivePath, environment.GamePath);
        var result = await installer.InstallAsync(analysis, new InstallOptions { AllowOverwriteConflicts = true });
        Assert.False(result.Success);
        Assert.True(result.WasRolledBack);
        Assert.Equal("original", await File.ReadAllTextAsync(existing));
        Assert.False(File.Exists(Path.Combine(environment.GamePath, "content", "cars", "rollback", "two.txt")));
    }

    private sealed class IncompleteArchiveService : IArchiveService
    {
        public string ArchivePath { get; set; } = string.Empty;
        private static readonly ArchiveEntryDescriptor[] Entries = [new("content/cars/rollback/one.txt", 3, 3, false), new("content/cars/rollback/two.txt", 3, 3, false)];
        public Task<ArchiveInspection> InspectAsync(string archivePath, CancellationToken cancellationToken = default) => Task.FromResult(new ArchiveInspection { ArchivePath = archivePath, Entries = Entries });
        public async Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            var file = Path.Combine(destinationDirectory, "content", "cars", "rollback", "one.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "new", cancellationToken);
        }
    }
}
