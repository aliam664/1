using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class ArchiveValidationTests
{
    [Fact]
    public async Task Inspect_RejectsPathTraversal()
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip("traversal.zip", ("../owned.txt", "bad"));
        await Assert.ThrowsAsync<UnsafeArchiveException>(() => environment.Get<IArchiveService>().InspectAsync(archive));
    }

    [Theory]
    [InlineData("payload.exe")]
    [InlineData("install.ps1")]
    [InlineData("shortcut.lnk")]
    public async Task Inspect_RejectsExecutableAndCommandContent(string dangerousName)
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip("danger.zip", ($"content/{dangerousName}", "bad"));
        await Assert.ThrowsAsync<UnsafeArchiveException>(() => environment.Get<IArchiveService>().InspectAsync(archive));
    }

    [Fact]
    public async Task Inspect_RejectsCorruptArchiveWithUnderstandableDomainError()
    {
        using var environment = new TestEnvironment();
        var archive = Path.Combine(environment.Root, "corrupt.zip");
        await File.WriteAllBytesAsync(archive, [0x50, 0x4B, 0x03, 0x04, 0x00]);
        var error = await Assert.ThrowsAsync<UnsafeArchiveException>(() => environment.Get<IArchiveService>().InspectAsync(archive));
        Assert.Contains("corrupt", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Extract_WritesOnlyInsideValidatedDestination()
    {
        using var environment = new TestEnvironment();
        var archive = environment.CreateZip("safe.zip", ("content/cars/test/data.acd", "safe"));
        var destination = Path.Combine(environment.Root, "staging");
        await environment.Get<IArchiveService>().ExtractAsync(archive, destination);
        Assert.Equal("safe", await File.ReadAllTextAsync(Path.Combine(destination, "content", "cars", "test", "data.acd")));
    }
}
