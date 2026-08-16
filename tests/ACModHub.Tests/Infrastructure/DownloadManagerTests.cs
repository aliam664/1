using System.Net;
using ACModHub.Core.Models;
using ACModHub.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ACModHub.Tests.Infrastructure;

public sealed class DownloadManagerTests
{
    [Fact]
    public async Task Download_FailsWhenServerEndsBeforeDeclaredContentLength()
    {
        var root = Path.Combine(Path.GetTempPath(), "acmodhub-download-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new IncompleteResponseHandler();
            using var http = new HttpClient(handler);
            using var manager = new HttpDownloadManager(http, new FileHashService(), new AppPaths(root), NullLogger<HttpDownloadManager>.Instance, 1, TimeSpan.FromMilliseconds(5));
            var failed = new TaskCompletionSource<DownloadJob>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.ProgressChanged += (_, progress) =>
            {
                var job = manager.Jobs.First(x => x.Request.Id == progress.JobId);
                if (progress.State == DownloadState.Failed) failed.TrySetResult(job);
            };
            await manager.EnqueueAsync(new DownloadRequest { Source = new Uri("https://example.invalid/incomplete.zip"), FileName = "incomplete.zip" });
            var result = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(DownloadState.Failed, result.State);
            Assert.Contains("expected bytes", result.Error!, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(result.DestinationPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class IncompleteResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent([1, 2, 3, 4, 5]);
            content.Headers.ContentLength = 10;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
