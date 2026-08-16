using ACModHub.UI.Services;

namespace ACModHub.UI.Tests;

public sealed class StoreBridgeTests
{
    [Fact]
    public void ValidInstallMod_IsParsed()
    {
        Assert.True(StoreBridge.TryParse("""{"command":"installMod","modId":"author.mod-1"}""", out var message, out _));
        Assert.NotNull(message);
        Assert.Equal(StoreBridgeCommand.InstallMod, message!.Command);
        Assert.Equal("author.mod-1", message.ModId);
    }

    [Theory]
    [InlineData("""{"command":"deleteAllFiles"}""")]
    [InlineData("""{"command":"installMod"}""")]
    [InlineData("""{"command":"installMod","modId":"../../etc/passwd"}""")]
    [InlineData("""{"command":"installMod","modId":"x"}""")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{"command":123}""")]
    public void InvalidMessages_AreRejected(string json)
    {
        Assert.False(StoreBridge.TryParse(json, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void OversizedMessage_IsRejected()
    {
        var huge = """{"command":"refreshCatalog","padding":"""" + new string('x', 5000) + """"}""";
        Assert.False(StoreBridge.TryParse(huge, out _, out _));
    }

    [Fact]
    public void RefreshAndCancel_AreParsed()
    {
        Assert.True(StoreBridge.TryParse("""{"command":"refreshCatalog"}""", out var refresh, out _));
        Assert.Equal(StoreBridgeCommand.RefreshCatalog, refresh!.Command);
        Assert.True(StoreBridge.TryParse("""{"command":"cancelOperation"}""", out var cancel, out _));
        Assert.Equal(StoreBridgeCommand.CancelOperation, cancel!.Command);
    }

    [Theory]
    [InlineData("https://github.com/aliam664/1/releases", true)]
    [InlineData("https://github.com/aliam664/Data", true)]
    [InlineData("https://aliam664.github.io/Data/", true)]
    [InlineData("http://github.com/aliam664/1", false)]
    [InlineData("https://evil.example/", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///c:/windows/system32", false)]
    public void ExternalUrls_AreAllowlisted(string url, bool expected)
    {
        Assert.Equal(expected, StoreBridge.IsTrustedExternalUrl(url));
    }

    [Fact]
    public void OpenExternal_RequiresTrustedUrl()
    {
        Assert.False(StoreBridge.TryParse("""{"command":"openExternal","url":"https://evil.example/"}""", out _, out _));
        Assert.True(StoreBridge.TryParse("""{"command":"openExternal","url":"https://github.com/aliam664/1"}""", out var message, out _));
        Assert.Equal(StoreBridgeCommand.OpenTrustedExternalLink, message!.Command);
    }
}
