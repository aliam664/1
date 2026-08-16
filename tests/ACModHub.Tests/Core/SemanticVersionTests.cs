using ACModHub.Core;

namespace ACModHub.Tests.Core;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("v2.0.0-preview.1", 2, 0, 0, "preview.1")]
    [InlineData("0.0.0-alpha", 0, 0, 0, "alpha")]
    public void Parses_ValidVersions(string text, int major, int minor, int patch, string? prerelease)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(prerelease, version.Prerelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("a.b.c")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+meta")]
    [InlineData("1.2.3-pre^")]
    [InlineData(null)]
    public void Rejects_InvalidVersions(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Fact]
    public void ReleaseBeatsPrerelease()
    {
        Assert.True(SemanticVersion.Parse("1.0.0") > SemanticVersion.Parse("1.0.0-rc.1"));
    }

    [Fact]
    public void PrereleaseIdentifiersCompareNumerically()
    {
        Assert.True(SemanticVersion.Parse("1.0.0-alpha.2") > SemanticVersion.Parse("1.0.0-alpha.10"));
        Assert.True(SemanticVersion.Parse("1.0.0-alpha") < SemanticVersion.Parse("1.0.0-alpha.1"));
        Assert.True(SemanticVersion.Parse("1.0.0-beta") < SemanticVersion.Parse("1.0.0-rc.1"));
    }

    [Fact]
    public void DowngradeDetection_Works()
    {
        var current = SemanticVersion.Parse("2.0.0-preview.1");
        Assert.False(SemanticVersion.Parse("2.0.0-preview.1") > current); // equal
        Assert.False(SemanticVersion.Parse("1.9.9") > current);           // older
        Assert.True(SemanticVersion.Parse("2.0.0") > current);            // newer (release)
        Assert.True(SemanticVersion.Parse("2.1.0-preview.1") > current);
    }

    [Fact]
    public void Equality_IsOrderBased()
    {
        Assert.True(SemanticVersion.Parse("v1.0.0") == SemanticVersion.Parse("1.0.0"));
        Assert.Equal(SemanticVersion.Parse("v1.0.0"), SemanticVersion.Parse("1.0.0"));
    }
}
