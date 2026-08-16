using System.Net;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Infrastructure.Services;
using ACModHub.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ACModHub.Tests.Infrastructure;

public sealed class UpdateCheckerTests
{
    private static readonly SemanticVersion CurrentVersion = SemanticVersion.Parse("1.0.0");

    private static GitHubUpdateChecker Create(FakeCatalogHandler handler, AppConfig? config = null, string? cacheRoot = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("updates").ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "acmodhub-tests", cacheRoot ?? Guid.NewGuid().ToString("N")));
        return new GitHubUpdateChecker(
            provider.GetRequiredService<IHttpClientFactory>(),
            paths,
            config ?? new AppConfig { UpdateRepository = "aliam664/1" },
            NullLogger<GitHubUpdateChecker>.Instance,
            () => CurrentVersion);
    }

    private static string Release(int index, string tag, bool prerelease = false, bool draft = false) =>
        $$"""
        [{"tag_name":"{{tag}}","prerelease":{{(prerelease ? "true" : "false")}},"draft":{{(draft ? "true" : "false")}},"name":"Release {{index}}","body":"notes {{index}}","published_at":"2026-08-0{{index}}T00:00:00Z","html_url":"https://github.com/aliam664/1/releases/tag/{{tag}}","assets":[{"name":"AC-Mod-Hub-Setup.exe","size":100,"browser_download_url":"https://github.com/aliam664/1/releases/download/{{tag}}/AC-Mod-Hub-Setup.exe"},{"name":"SHA256SUMS.txt","size":10,"browser_download_url":"https://github.com/aliam664/1/releases/download/{{tag}}/SHA256SUMS.txt"}]}]
        """;

    private static readonly string Feed = "["
        + Release(1, "v1.0.0")[1..^1] + ","
        + Release(2, "v1.5.0")[1..^1] + ","
        + Release(3, "v2.0.0-preview.1", prerelease: true)[1..^1] + ","
        + Release(4, "v2.0.0")[1..^1] + ","
        + Release(5, "v9.9.9", draft: true)[1..^1]
        + "]";

    [Fact]
    public async Task StableChannel_ExcludesPrereleaseAndDrafts()
    {
        var checker = Create(new FakeCatalogHandler().EnqueueJson(Feed));
        var result = await checker.CheckAsync(UpdateChannel.Stable, true);
        Assert.True(result.HasUpdate);
        Assert.Equal("2.0.0", result.Release!.Version.ToString());
    }

    [Fact]
    public async Task BetaChannel_IncludesPrereleases()
    {
        var checker = Create(new FakeCatalogHandler().EnqueueJson(Feed));
        var result = await checker.CheckAsync(UpdateChannel.Beta, true);
        Assert.True(result.HasUpdate);
        Assert.Equal("2.0.0", result.Release!.Version.ToString()); // release beats prerelease of same base
    }

    [Fact]
    public async Task Downgrade_IsNeverOffered()
    {
        var feed = "[" + Release(1, "v0.9.9")[1..^1] + "]";
        var checker = Create(new FakeCatalogHandler().EnqueueJson(feed));
        var result = await checker.CheckAsync(UpdateChannel.Stable, true);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task SameVersion_IsNotAnUpdate()
    {
        var feed = "[" + Release(1, "v1.0.0")[1..^1] + "]";
        var checker = Create(new FakeCatalogHandler().EnqueueJson(feed));
        var result = await checker.CheckAsync(UpdateChannel.Stable, true);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task BetaChannel_OffersPrereleaseWhenItIsNewer()
    {
        var feed = "[" + Release(1, "v1.5.0-preview.2", prerelease: true)[1..^1] + "]";
        var checker = Create(new FakeCatalogHandler().EnqueueJson(feed));
        var result = await checker.CheckAsync(UpdateChannel.Beta, true);
        Assert.True(result.HasUpdate);
        Assert.True(result.IsPrereleaseUpdate);
        Assert.Equal("1.5.0-preview.2", result.Release!.Version.ToString());
    }

    [Fact]
    public async Task StableChannel_DoesNotOfferPrerelease()
    {
        var feed = "[" + Release(1, "v1.5.0-preview.2", prerelease: true)[1..^1] + "]";
        var checker = Create(new FakeCatalogHandler().EnqueueJson(feed));
        var result = await checker.CheckAsync(UpdateChannel.Stable, true);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task FeedWithoutAssets_DoesNotOfferInstaller()
    {
        var feed = """[{"tag_name":"v3.0.0","prerelease":false,"draft":false,"name":"R","body":"","published_at":"2026-08-01T00:00:00Z","html_url":"https://github.com/aliam664/1/releases/tag/v3.0.0","assets":[]}]""";
        var checker = Create(new FakeCatalogHandler().EnqueueJson(feed));
        var result = await checker.CheckAsync(UpdateChannel.Stable, true);
        Assert.True(result.HasUpdate);
        Assert.Null(result.Release!.InstallerAsset);
        Assert.Null(result.Release.ChecksumsAsset);
    }

    [Fact]
    public async Task NotModified_UsesCachedFeed()
    {
        var cacheRoot = "updates-" + Guid.NewGuid().ToString("N");
        var first = Create(new FakeCatalogHandler().EnqueueJson(Feed, "\"etag-updates\""), cacheRoot: cacheRoot);
        var firstResult = await first.CheckAsync(UpdateChannel.Stable, true);
        Assert.True(firstResult.HasUpdate);

        // Same cache directory, server replies 304: the cached feed must be reused.
        var second = Create(new FakeCatalogHandler().Enqueue(HttpStatusCode.NotModified), cacheRoot: cacheRoot);
        var cached = await second.CheckAsync(UpdateChannel.Stable, false);
        Assert.True(cached.HasUpdate);
        Assert.Equal("2.0.0", cached.Release!.Version.ToString());
    }

    [Fact]
    public async Task HttpDowngradeRedirect_IsRejected()
    {
        var handler = new FakeCatalogHandler().EnqueueRedirect("http://evil.example/releases");
        var checker = Create(handler);
        await Assert.ThrowsAsync<ModHubException>(() => checker.CheckAsync(UpdateChannel.Stable, true));
    }

    [Fact]
    public async Task NetworkFailure_WithoutCache_Throws()
    {
        var checker = Create(new FakeCatalogHandler()); // no responses → 404
        await Assert.ThrowsAsync<ModHubException>(() => checker.CheckAsync(UpdateChannel.Stable, true));
    }

    [Fact]
    public async Task InvalidSemverTags_AreSkipped()
    {
        var feed = """[{"tag_name":"not-a-version","prerelease":false,"draft":false,"name":"R","body":"","published_at":"2026-08-01T00:00:00Z","html_url":"https://github.com/aliam664/1/releases/tag/x","assets":[]}]""";
        var checker = Create(new FakeCatalogHandler().EnqueueJson(feed));
        var result = await checker.CheckAsync(UpdateChannel.Stable, true);
        Assert.False(result.HasUpdate);
    }
}
