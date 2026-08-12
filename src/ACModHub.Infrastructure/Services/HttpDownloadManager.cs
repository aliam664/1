using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

public sealed class HttpDownloadManager : IDownloadManager, IDisposable
{
    private const int MaximumRetries = 3;
    private readonly HttpClient _httpClient;
    private readonly IFileHashService _hashes;
    private readonly IAppPaths _paths;
    private readonly ILogger<HttpDownloadManager> _logger;
    private readonly ConcurrentDictionary<Guid, DownloadJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _tokens = new();
    private readonly SemaphoreSlim _concurrency;
    private bool _disposed;

    public HttpDownloadManager(HttpClient httpClient, IFileHashService hashes, IAppPaths paths, ILogger<HttpDownloadManager> logger, int maxConcurrency = 3)
    {
        _httpClient = httpClient;
        _hashes = hashes;
        _paths = paths;
        _logger = logger;
        _concurrency = new SemaphoreSlim(Math.Clamp(maxConcurrency, 1, 8));
    }

    public event EventHandler<DownloadProgress>? ProgressChanged;
    public IReadOnlyCollection<DownloadJob> Jobs => _jobs.Values.OrderByDescending(x => x.CreatedAt).ToArray();

    public Task<Guid> EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Source.Scheme is not ("http" or "https")) throw new ArgumentException("Only HTTP and HTTPS URLs are accepted.", nameof(request));
        var safeName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName != request.FileName) throw new ArgumentException("The download file name is invalid.", nameof(request));
        var destination = Path.Combine(_paths.DownloadsDirectory, $"{request.Id:N}-{safeName}");
        var job = new DownloadJob { Request = request, DestinationPath = destination };
        if (!_jobs.TryAdd(request.Id, job)) throw new InvalidOperationException("A download with this ID already exists.");
        Start(job);
        return Task.FromResult(request.Id);
    }

    public Task PauseAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = RequiredJob(jobId);
        if (job.State is DownloadState.Downloading or DownloadState.Queued)
        {
            job.State = DownloadState.Paused;
            if (_tokens.TryGetValue(jobId, out var token)) token.Cancel();
            Raise(job);
        }
        return Task.CompletedTask;
    }

    public Task ResumeAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = RequiredJob(jobId);
        if (job.State is DownloadState.Paused or DownloadState.Failed)
        {
            job.State = DownloadState.Queued;
            job.Error = null;
            Start(job);
        }
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = RequiredJob(jobId);
        job.State = DownloadState.Cancelled;
        if (_tokens.TryGetValue(jobId, out var token)) token.Cancel();
        var part = job.DestinationPath + ".part";
        try { if (File.Exists(part)) File.Delete(part); } catch (IOException ex) { _logger.LogWarning(ex, "Could not remove partial download {Path}", part); }
        Raise(job);
        return Task.CompletedTask;
    }

    public Task RetryAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = RequiredJob(jobId);
        if (job.State is not (DownloadState.Failed or DownloadState.Cancelled)) return Task.CompletedTask;
        job.RetryCount = 0;
        job.Error = null;
        job.State = DownloadState.Queued;
        Start(job);
        return Task.CompletedTask;
    }

    private void Start(DownloadJob job)
    {
        var source = new CancellationTokenSource();
        if (_tokens.TryGetValue(job.Request.Id, out var old)) { old.Dispose(); _tokens[job.Request.Id] = source; }
        else _tokens.TryAdd(job.Request.Id, source);
        _ = RunWithRetryAsync(job, source.Token);
    }

    private async Task RunWithRetryAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        try
        {
            await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (;;)
                {
                    try
                    {
                        await DownloadAsync(job, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                    catch (Exception ex) when (ex is HttpRequestException or IOException)
                    {
                        job.RetryCount++;
                        job.Error = ex.Message;
                        if (job.RetryCount >= MaximumRetries)
                        {
                            job.State = DownloadState.Failed;
                            Raise(job);
                            _logger.LogError(ex, "Download {DownloadId} failed", job.Request.Id);
                            return;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, job.RetryCount)), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally { _concurrency.Release(); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task DownloadAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        if (job.State == DownloadState.Cancelled) return;
        job.State = DownloadState.Downloading;
        Raise(job);
        var destination = job.DestinationPath!;
        var partial = destination + ".part";
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, job.Request.Source);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (existing > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(partial);
            throw new IOException("The server rejected the saved partial range; retrying from the beginning.");
        }
        response.EnsureSuccessStatusCode();
        var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append) existing = 0;
        job.TotalBytes = response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength.HasValue ? existing + response.Content.Headers.ContentLength.Value : null);
        job.BytesReceived = existing;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[128 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            job.BytesReceived += read;
            Raise(job);
        }
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);

        job.State = DownloadState.Verifying;
        Raise(job);
        if (!string.IsNullOrWhiteSpace(job.Request.ExpectedSha256))
        {
            var actual = await _hashes.ComputeSha256Async(partial, cancellationToken).ConfigureAwait(false);
            if (!actual.Equals(job.Request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The downloaded file failed SHA-256 verification.");
        }
        File.Move(partial, destination, true);
        job.State = DownloadState.Completed;
        job.Error = null;
        Raise(job);
    }

    private DownloadJob RequiredJob(Guid id) => _jobs.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The download job does not exist.");

    private void Raise(DownloadJob job)
    {
        var percentage = job.TotalBytes is > 0 ? job.BytesReceived * 100d / job.TotalBytes.Value : null;
        ProgressChanged?.Invoke(this, new DownloadProgress(job.Request.Id, job.State, job.BytesReceived, job.TotalBytes, percentage));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var token in _tokens.Values) { token.Cancel(); token.Dispose(); }
        _concurrency.Dispose();
    }
}

public sealed class DirectUrlContentProvider : IContentProvider
{
    public string Name => "Direct HTTP";
    public Task<IReadOnlyList<ContentRelease>> CheckUpdatesAsync(IEnumerable<ModManifest> installedMods, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedMods);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ContentRelease>>([]);
    }
}
