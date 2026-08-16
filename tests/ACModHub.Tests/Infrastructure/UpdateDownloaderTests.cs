using System.Net;
using System.Security.Cryptography;
using System.Text;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Infrastructure.Services;
using ACModHub.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ACModHub.Tests.Infrastructure;

public sealed class UpdateDownloaderTests
{
    private const string PackageBytes = "AC-Mod-Hub-update-package-bytes-v2";
    private static readonly string PackageSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PackageBytes))).ToLowerInvariant();

    private static UpdateReleaseInfo Release(long size = PackageBytes.Length) => new()
    {
        Version = SemanticVersion.Parse("2.0.0"),
        Tag = "v2.0.0",
        Title = "2.0.0",
        ReleaseNotes = string.Empty,
        PublishedAt = DateTimeOffset.UtcNow,
        HtmlUrl = new Uri("https://github.com/aliam664/1/releases/tag/v2.0.0"),
        Assets =
        [
            new UpdateAsset(AppInfo.InstallerAssetName, size, new Uri("https://github.com/aliam664/1/releases/download/v2.0.0/AC-Mod-Hub-Setup.exe")),
            new UpdateAsset(AppInfo.ChecksumsAssetName, 1024, new Uri("https://github.com/aliam664/1/releases/download/v2.0.0/SHA256SUMS.txt"))
        ]
    };

    private static UpdateDownloader Create(FakeCatalogHandler handler, IDiskSpaceService? diskSpace = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("updates").ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "acmodhub-tests", Guid.NewGuid().ToString("N")));
        return new UpdateDownloader(
            provider.GetRequiredService<IHttpClientFactory>(),
            paths,
            new FileHashService(),
            diskSpace ?? new DiskSpaceService(),
            NullLogger<UpdateDownloader>.Instance);
    }

    private static string Sums(string fileName, string sha) => $"{sha} *{fileName}\n";

    [Fact]
    public async Task DownloadsVerifiesAndReturnsPackage()
    {
        var handler = new FakeCatalogHandler()
            .Enqueue(HttpStatusCode.OK, Sums(AppInfo.InstallerAssetName, PackageSha256), new Dictionary<string, string> { ["Content-Type"] = "text/plain" })
            .Enqueue(HttpStatusCode.OK, PackageBytes, new Dictionary<string, string> { ["Content-Type"] = "application/octet-stream" })
            .Enqueue(HttpStatusCode.OK, PackageBytes, new Dictionary<string, string> { ["Content-Type"] = "application/octet-stream" });
        var downloader = Create(handler);
        var path = await downloader.DownloadAsync(Release());
        Assert.True(File.Exists(path));
        Assert.EndsWith("AC-Mod-Hub-Setup-v2.0.0.exe", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HashMismatch_DeletesPartialAndThrows()
    {
        var handler = new FakeCatalogHandler()
            .Enqueue(HttpStatusCode.OK, Sums(AppInfo.InstallerAssetName, new string('0', 64)), new Dictionary<string, string> { ["Content-Type"] = "text/plain" })
            .Enqueue(HttpStatusCode.OK, PackageBytes, new Dictionary<string, string> { ["Content-Type"] = "application/octet-stream" })
            .Enqueue(HttpStatusCode.OK, PackageBytes, new Dictionary<string, string> { ["Content-Type"] = "application/octet-stream" });
        var downloader = Create(handler);
        await Assert.ThrowsAsync<ModHubException>(() => downloader.DownloadAsync(Release()));
        Assert.Empty(Directory.EnumerateFiles(downloader.UpdatesDirectory));
    }

    [Fact]
    public async Task MissingSumsEntry_IsRejected()
    {
        var handler = new FakeCatalogHandler()
            .Enqueue(HttpStatusCode.OK, Sums("other-file.exe", new string('1', 64)), new Dictionary<string, string> { ["Content-Type"] = "text/plain" });
        var downloader = Create(handler);
        await Assert.ThrowsAsync<ModHubException>(() => downloader.DownloadAsync(Release()));
    }

    [Fact]
    public async Task SizeMismatch_IsRejected()
    {
        var handler = new FakeCatalogHandler()
            .Enqueue(HttpStatusCode.OK, Sums(AppInfo.InstallerAssetName, PackageSha256), new Dictionary<string, string> { ["Content-Type"] = "text/plain" })
            .Enqueue(HttpStatusCode.OK, PackageBytes, new Dictionary<string, string> { ["Content-Type"] = "application/octet-stream" })
            .Enqueue(HttpStatusCode.OK, PackageBytes, new Dictionary<string, string> { ["Content-Type"] = "application/octet-stream" });
        var downloader = Create(handler);
        await Assert.ThrowsAsync<ModHubException>(() => downloader.DownloadAsync(Release(size: PackageBytes.Length + 10)));
    }

    [Fact]
    public async Task InsufficientDiskSpace_IsRejectedBeforeDownload()
    {
        var downloader = Create(new FakeCatalogHandler(), new ZeroDiskSpaceService());
        await Assert.ThrowsAsync<ModHubException>(() => downloader.DownloadAsync(Release()));
    }

    [Fact]
    public async Task HttpDowngradeRedirect_IsRejected()
    {
        var handler = new FakeCatalogHandler()
            .EnqueueRedirect("http://evil.example/sums.txt");
        var downloader = Create(handler);
        await Assert.ThrowsAsync<ModHubException>(() => downloader.DownloadAsync(Release()));
    }

    private sealed class ZeroDiskSpaceService : IDiskSpaceService
    {
        public long GetAvailableBytes(string path) => 0;
    }
}
