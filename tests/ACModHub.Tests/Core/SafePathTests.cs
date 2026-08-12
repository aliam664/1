using ACModHub.Core;
using ACModHub.Core.Services;

namespace ACModHub.Tests.Core;

public sealed class SafePathTests
{
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("folder/../../outside.txt")]
    [InlineData("C:/Windows/file.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("folder/file.txt:stream")]
    [InlineData("CON/file.txt")]
    [InlineData("folder./file.txt")]
    public void NormalizeRelative_RejectsUnsafeWindowsPaths(string path) => Assert.Throws<UnsafeArchiveException>(() => SafePath.NormalizeRelative(path));

    [Fact]
    public void CombineUnderRoot_ReturnsContainedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "safe-root");
        var actual = SafePath.CombineUnderRoot(root, "content/cars/car/data.acd");
        Assert.StartsWith(Path.GetFullPath(root), actual, StringComparison.OrdinalIgnoreCase);
    }
}
