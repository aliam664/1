using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;
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
    private readonly Func<int> _maxConcurrencyProvider;
    private readonly TimeSpan _retryBaseDelay;
    private SemaphoreSlim? _concurrency;
    private bool _disposed;

    public HttpDownloadManager(HttpClient httpClient, IFileHashService hashes, IAppPaths paths, ILogger<HttpDownloadManager> logger, Func<int>? maxConcurrencyProvider = null, TimeSpan? retryBaseDelay = null)
    {
        _httpClient = httpClient;
        _hashes = hashes;
        _paths = paths;
        _logger = logger;
        // Lazy provider: the semaphore is created on first use so constructing the manager
        // never blocks on settings I/O.
        _maxConcurrencyProvider = maxConcurrencyProvider ?? (() => 3);
        _retryBaseDelay = retryBaseDelay ?? TimeSpan.FromSeconds(1);
    }

    private SemaphoreSlim Concurrency => _concurrency ??= new SemaphoreSlim(Math.Clamp(_maxConcurrencyProvider(), 1, 8));

    public event EventHandler<DownloadProgress>? ProgressChanged;
    public IReadOnlyCollection<DownloadJob> Jobs => _jobs.Values.OrderByDescending(x => x.CreatedAt).ToArray();

    public Task<Guid> EnqueueAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Source.Scheme is not ("http" or "https")) throw new ArgumentException("Only HTTP and HTTPS URLs are accepted.", nameof(request));
        if (request.ExpectedSize is <= 0) throw new ArgumentException("Expected download size must be positive when provided.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256) && (request.ExpectedSha256.Length != 64 || request.ExpectedSha256.Any(x => !Uri.IsHexDigit(x))))
            throw new ArgumentException("Expected SHA-256 must contain exactly 64 hexadecimal characters.", nameof(request));
        if (!SafePath.IsSafeFileName(request.FileName))
            throw new ArgumentException("The download file name is invalid.", nameof(request));
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
        // Terminal-state protection: cancelling a finished download must never touch
        // the verified file or corrupt the state.
        if (job.State is DownloadState.Completed or DownloadState.Cancelled or DownloadState.Verifying) return Task.CompletedTask;
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
            await Concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                    catch (DownloadValidationException ex)
                    {
                        // The bytes on disk failed validation: discard the partial so a
                        // later retry starts clean instead of reusing corrupt data.
                        try { if (File.Exists(job.DestinationPath + ".part")) File.Delete(job.DestinationPath + ".part"); } catch (IOException) { }
                        job.Error = ex.Message;
                        job.State = DownloadState.Failed;
                        Raise(job);
                        _logger.LogError(ex, "Download {DownloadId} failed validation", job.Request.Id);
                        return;
                    }
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
                        var retryDelay = TimeSpan.FromMilliseconds(_retryBaseDelay.TotalMilliseconds * Math.Pow(2, job.RetryCount - 1));
                        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally { Concurrency.Release(); }
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
        job.SpeedBytesPerSecond = null;
        job.EstimatedTimeRemaining = null;
        var sessionStartBytes = existing;
        var stopwatch = Stopwatch.StartNew();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[128 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            job.BytesReceived += read;
            if (stopwatch.Elapsed.TotalSeconds >= 0.2)
            {
                var transferred = job.BytesReceived - sessionStartBytes;
                job.SpeedBytesPerSecond = transferred / stopwatch.Elapsed.TotalSeconds;
                job.EstimatedTimeRemaining = job.TotalBytes.HasValue && job.SpeedBytesPerSecond > 0
                    ? TimeSpan.FromSeconds(Math.Max(0, job.TotalBytes.Value - job.BytesReceived) / job.SpeedBytesPerSecond.Value)
                    : null;
            }
            Raise(job);
        }
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (job.TotalBytes.HasValue && job.BytesReceived != job.TotalBytes.Value)
            throw new IOException($"The download ended at {job.BytesReceived:N0} of {job.TotalBytes.Value:N0} expected bytes.");
        if (job.Request.ExpectedSize.HasValue && job.BytesReceived != job.Request.ExpectedSize.Value)
            throw new DownloadValidationException($"The downloaded file size ({job.BytesReceived:N0}) does not match the package metadata ({job.Request.ExpectedSize.Value:N0}).");

        job.State = DownloadState.Verifying;
        Raise(job);
        if (!string.IsNullOrWhiteSpace(job.Request.ExpectedSha256))
        {
            var actual = await _hashes.ComputeSha256Async(partial, cancellationToken).ConfigureAwait(false);
            if (!actual.Equals(job.Request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new DownloadValidationException("The downloaded file failed SHA-256 verification.");
        }
        File.Move(partial, destination, true);
        job.State = DownloadState.Completed;
        job.EstimatedTimeRemaining = TimeSpan.Zero;
        job.Error = null;
        Raise(job);
    }

    private sealed class DownloadValidationException(string message) : Exception(message);

    private DownloadJob RequiredJob(Guid id) => _jobs.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The download job does not exist.");

    private void Raise(DownloadJob job)
    {
        // Subscriber isolation: one failing subscriber must never break the download loop
        // or prevent other subscribers from receiving progress.
        var handlers = ProgressChanged?.GetInvocationList() ?? [];
        foreach (var handler in handlers)
        {
            try
            {
                ((EventHandler<DownloadProgress>)handler)(this, new DownloadProgress(job.Request.Id, job.State, job.BytesReceived, job.TotalBytes,
                    job.TotalBytes is > 0 ? job.BytesReceived * 100d / job.TotalBytes.Value : null, job.SpeedBytesPerSecond, job.EstimatedTimeRemaining));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A download progress subscriber threw an exception");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var token in _tokens.Values) { token.Cancel(); token.Dispose(); }
        _concurrency?.Dispose();
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
