using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

/// <summary>
/// Downloads a launcher update package and verifies its SHA-256 against the
/// SHA256SUMS.txt asset published with the GitHub Release. Supports resuming via
/// HTTP Range, cancellation with .part cleanup, and disk-space checks.
/// </summary>
public sealed partial class UpdateDownloader : IUpdateDownloader
{
    private const int ChecksumsMaximumBytes = 256 * 1024;
    private const long DiskMarginBytes = 256L * 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppPaths _paths;
    private readonly IFileHashService _hashes;
    private readonly IDiskSpaceService _diskSpace;
    private readonly AppConfig _config;
    private readonly ILogger<UpdateDownloader> _logger;

    public UpdateDownloader(IHttpClientFactory httpClientFactory, IAppPaths paths, IFileHashService hashes, IDiskSpaceService diskSpace, AppConfig config, ILogger<UpdateDownloader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _paths = paths;
        _hashes = hashes;
        _diskSpace = diskSpace;
        _config = config;
        _logger = logger;
    }

    public string UpdatesDirectory => Path.Combine(_paths.CacheDirectory, "updates");

    public async Task<string> DownloadAsync(UpdateReleaseInfo release, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        var installer = release.InstallerAsset ?? throw new ModHubException("The release has no installer asset.");
        var checksums = release.ChecksumsAsset ?? throw new ModHubException("The release has no SHA256SUMS asset; integrity cannot be verified.");

        Directory.CreateDirectory(UpdatesDirectory);
        var destination = Path.Combine(UpdatesDirectory, $"{AppInfo.InstallerAssetName[..^4]}-{release.Tag}.exe");
        var partial = destination + ".part";

        // 1. Fetch and parse SHA256SUMS.txt to obtain the expected installer hash.
        var expectedSha256 = await FetchExpectedSha256Async(checksums.DownloadUrl, installer.Name, cancellationToken).ConfigureAwait(false);

        // 2. Disk space check before starting the download.
        var required = installer.Size + DiskMarginBytes;
        var available = _diskSpace.GetAvailableBytes(UpdatesDirectory);
        if (available < required)
            throw new ModHubException($"Not enough disk space for the update: {required - available:N0} additional bytes required.");

        // 3. Download with resume support.
        var client = _httpClientFactory.CreateClient("updates");
        var requestUri = await ResolveWithHttpsOnly(client, installer.DownloadUrl, cancellationToken).ConfigureAwait(false);
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (existing > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(partial);
            existing = 0;
            using var retry = new HttpRequestMessage(HttpMethod.Get, requestUri);
            using var retryResponse = await client.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            retryResponse.EnsureSuccessStatusCode();
            await CopyToFileAsync(retryResponse, partial, append: false, installer.Size, progress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            response.EnsureSuccessStatusCode();
            var append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            await CopyToFileAsync(response, partial, append, installer.Size, progress, cancellationToken).ConfigureAwait(false);
        }

        // 4. Integrity verification. The package is never marked ready without a matching hash.
        try
        {
            return await VerifyAndFinalizeAsync(partial, destination, expectedSha256, installer.Size, progress, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Cancellation or verification failure: never leave partial state behind.
            try { if (File.Exists(partial)) File.Delete(partial); } catch (IOException) { }
            throw;
        }
    }

    private async Task<string> VerifyAndFinalizeAsync(string partial, string destination, string expectedSha256, long expectedSize, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        Report(progress, UpdateDownloadState.Verifying, existing: 0, total: expectedSize);
        var actual = await _hashes.ComputeSha256Async(partial, cancellationToken).ConfigureAwait(false);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(partial); } catch (IOException) { }
            throw new ModHubException("The downloaded update failed SHA-256 verification and was discarded.");
        }
        if (new FileInfo(partial).Length != expectedSize)
        {
            try { File.Delete(partial); } catch (IOException) { }
            throw new ModHubException("The downloaded update size does not match the release metadata.");
        }
        File.Move(partial, destination, true);
        Report(progress, UpdateDownloadState.ReadyToApply, existing: installer.Size, total: installer.Size);
        return destination;
    }

    public void Cleanup(string keepPath)
    {
        try
        {
            if (!Directory.Exists(UpdatesDirectory)) return;
            foreach (var file in Directory.EnumerateFiles(UpdatesDirectory, AppInfo.InstallerAssetName[..^4] + "-*.exe"))
            {
                if (string.Equals(file, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(file); } catch (IOException) { }
                var part = file + ".part";
                try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Update package cleanup failed");
        }
    }

    private async Task<string> FetchExpectedSha256Async(Uri checksumsUrl, string installerName, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("updates");
        var requestUri = await ResolveWithHttpsOnly(client, checksumsUrl, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > ChecksumsMaximumBytes)
            throw new ModHubException("The SHA256SUMS asset is unexpectedly large.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (content.Length > ChecksumsMaximumBytes) throw new ModHubException("The SHA256SUMS asset exceeded its read limit.");

        foreach (var line in content.Split('\n'))
        {
            var match = ChecksumLineRegex().Match(line.Trim());
            if (!match.Success) continue;
            if (match.Groups[2].Value.Equals(installerName, StringComparison.OrdinalIgnoreCase))
                return match.Groups[1].Value;
        }
        throw new ModHubException($"SHA256SUMS.txt does not contain an entry for {installerName}; the update cannot be verified.");
    }

    private static async Task<Uri> ResolveWithHttpsOnly(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        var current = uri;
        for (var hop = 0; hop < 8; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, current);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                if (location is null) throw new ModHubException("The update asset redirect has no Location header.");
                if (!location.IsAbsoluteUri) location = new Uri(current, location);
                if (location.Scheme != Uri.UriSchemeHttps) throw new ModHubException("The update asset attempted a non-HTTPS redirect.");
                current = location;
                continue;
            }
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent or HttpStatusCode.RangeNotSatisfiable) return current;
            // Some asset CDNs reject HEAD; fall back to the HTTPS URI and let GET validate it.
            if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.Forbidden) return current;
            response.EnsureSuccessStatusCode();
            return current;
        }
        throw new ModHubException("The update asset redirected too many times.");
    }

    private static async Task CopyToFileAsync(HttpResponseMessage response, string partialPath, bool append, long expectedTotal, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var existing = append && File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        var total = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength is { } length ? existing + length : expectedTotal;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(partialPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var received = existing;
        var stopwatch = Stopwatch.StartNew();
        var buffer = new byte[128 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (stopwatch.ElapsedMilliseconds >= 200 || received == total)
            {
                var speed = received > 0 ? received / stopwatch.Elapsed.TotalSeconds : 0;
                Report(progress, UpdateDownloadState.Downloading, received, total, speed);
            }
        }
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Report(IProgress<UpdateDownloadProgress>? progress, UpdateDownloadState state, long existing, long total, double? speed = null)
    {
        double? percentage = total > 0 ? existing * 100d / total : null;
        TimeSpan? eta = speed is > 0 && total > existing ? TimeSpan.FromSeconds((total - existing) / speed.Value) : null;
        progress?.Report(new UpdateDownloadProgress(state, existing, total, percentage, speed, eta));
    }

    [GeneratedRegex(@"^([0-9a-fA-F]{64})[ \t]+\*?([^\s]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumLineRegex();
}
