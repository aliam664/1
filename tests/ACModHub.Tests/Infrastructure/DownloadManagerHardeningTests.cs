using System.Net;
using System.Text;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;
using ACModHub.Infrastructure.Services;
using ACModHub.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ACModHub.Tests.Infrastructure;

public sealed class DownloadManagerHardeningTests
{
    private const string Content = "download-payload-for-hardening-tests";

    private static HttpDownloadManager Create(FakeCatalogHandler handler, string root)
    {
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        return new HttpDownloadManager(http, new FileHashService(), new AppPaths(root), NullLogger<HttpDownloadManager>.Instance, () => 1, TimeSpan.FromMilliseconds(5));
    }

    private static async Task WaitForTerminalAsync(HttpDownloadManager manager, Guid id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var job = manager.Jobs.First(x => x.Request.Id == id);
            if (job.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Download did not reach a terminal state in time.");
    }

    [Theory]
    [InlineData("con.zip")]
    [InlineData("NUL.7z")]
    [InlineData("com1.rar")]
    [InlineData("../evil.zip")]
    [InlineData("a/b.zip")]
    [InlineData("trailing.zip.")]
    [InlineData("trailing.zip ")]
    [InlineData("stream:ads.zip")]
    public void UnsafeFileNames_AreRejected(string fileName)
    {
        using var manager = Create(new FakeCatalogHandler(), Path.Combine(Path.GetTempPath(), "acmodhub-tests", Guid.NewGuid().ToString("N")));
        var request = new DownloadRequest { Source = new Uri("https://example.com/a.zip"), FileName = fileName };
        Assert.ThrowsAny<ArgumentException>(() => manager.EnqueueAsync(request).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task CancelAfterCompleted_KeepsFileAndState()
    {
        using var manager = Create(new FakeCatalogHandler().Enqueue(HttpStatusCode.OK, Content, null),
            Path.Combine(Path.GetTempPath(), "acmodhub-tests", Guid.NewGuid().ToString("N")));
        var id = await manager.EnqueueAsync(new DownloadRequest { Source = new Uri("https://example.com/a.zip"), FileName = "a.zip" });
        await WaitForTerminalAsync(manager, id);
        var job = manager.Jobs.First(x => x.Request.Id == id);
        Assert.Equal(DownloadState.Completed, job.State);
        Assert.True(File.Exists(job.DestinationPath));

        await manager.CancelAsync(id);
        var after = manager.Jobs.First(x => x.Request.Id == id);
        Assert.Equal(DownloadState.Completed, after.State);
        Assert.True(File.Exists(after.DestinationPath));
    }

    [Fact]
    public async Task SubscriberExceptions_DoNotBreakOtherSubscribers()
    {
        using var manager = Create(new FakeCatalogHandler().Enqueue(HttpStatusCode.OK, Content, null),
            Path.Combine(Path.GetTempPath(), "acmodhub-tests", Guid.NewGuid().ToString("N")));
        var received = 0;
        var throwing = 0;
        manager.ProgressChanged += (_, _) => { received++; };
        manager.ProgressChanged += (_, _) => { throwing++; throw new InvalidOperationException("subscriber bug"); };
        var id = await manager.EnqueueAsync(new DownloadRequest { Source = new Uri("https://example.com/a.zip"), FileName = "a.zip" });
        await WaitForTerminalAsync(manager, id);
        Assert.True(received > 0);
        Assert.True(throwing > 0);
        Assert.Equal(DownloadState.Completed, manager.Jobs.First(x => x.Request.Id == id).State);
    }

    [Fact]
    public async Task ValidationFailure_DeletesPartialFile()
    {
        using var manager = Create(new FakeCatalogHandler().Enqueue(HttpStatusCode.OK, Content, null),
            Path.Combine(Path.GetTempPath(), "acmodhub-tests", Guid.NewGuid().ToString("N")));
        var id = await manager.EnqueueAsync(new DownloadRequest
        {
            Source = new Uri("https://example.com/a.zip"),
            FileName = "a.zip",
            ExpectedSha256 = new string('a', 64)
        });
        await WaitForTerminalAsync(manager, id);
        var job = manager.Jobs.First(x => x.Request.Id == id);
        Assert.Equal(DownloadState.Failed, job.State);
        Assert.False(File.Exists(job.DestinationPath + ".part"));
        Assert.False(File.Exists(job.DestinationPath));
    }

    [Fact]
    public async Task PauseResume_CompletesDownload()
    {
        var handler = new FakeCatalogHandler(repeatLast: true);
        handler.Enqueue(HttpStatusCode.OK, Content, null);
        using var manager = Create(handler, Path.Combine(Path.GetTempPath(), "acmodhub-tests", Guid.NewGuid().ToString("N")));
        var id = await manager.EnqueueAsync(new DownloadRequest { Source = new Uri("https://example.com/a.zip"), FileName = "a.zip" });
        await manager.PauseAsync(id);
        Assert.Equal(DownloadState.Paused, manager.Jobs.First(x => x.Request.Id == id).State);
        await manager.ResumeAsync(id);
        await WaitForTerminalAsync(manager, id);
        Assert.Equal(DownloadState.Completed, manager.Jobs.First(x => x.Request.Id == id).State);
    }

    [Fact]
    public async Task ExpectedSizeMismatch_FailsCleanly()
    {
        using var manager = Create(new FakeCatalogHandler().Enqueue(HttpStatusCode.OK, Content, null),
            Path.Combine(Path.GetTempPath(), "acmodhub-tests", Guid.NewGuid().ToString("N")));
        var id = await manager.EnqueueAsync(new DownloadRequest
        {
            Source = new Uri("https://example.com/a.zip"),
            FileName = "a.zip",
            ExpectedSize = Content.Length + 50
        });
        await WaitForTerminalAsync(manager, id);
        Assert.Equal(DownloadState.Failed, manager.Jobs.First(x => x.Request.Id == id).State);
    }

    [Fact]
    public void SafeFileName_AcceptsNormalNames()
    {
        Assert.True(SafePath.IsSafeFileName("author.mod-id-v1.0.0.zip"));
        Assert.True(SafePath.IsSafeFileName("AC-Mod-Hub-Setup.exe"));
        Assert.False(SafePath.IsSafeFileName(""));
        Assert.False(SafePath.IsSafeFileName("   "));
    }
}
