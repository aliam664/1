using System.Security.Cryptography;
using System.Text;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

/// <summary>
/// Bounded, validated cache for catalog cover images (used by the native store fallback).
/// Only HTTPS URLs on trusted hosts are fetched; responses are limited by byte count and
/// content type; failed downloads fall back to the UI placeholder. Old entries are pruned.
/// </summary>
public interface ICoverImageService
{
    /// <summary>Returns a local cached file path for the cover, or null when unavailable.</summary>
    Task<string?> GetCachedCoverAsync(Uri coverUri, CancellationToken cancellationToken = default);
    /// <summary>Removes stale entries beyond the retention window and total size budget.</summary>
    Task CleanupAsync(CancellationToken cancellationToken = default);
}

public sealed class CoverImageService : ICoverImageService
{
    private const long MaximumImageBytes = 5 * 1024 * 1024;
    private const long CacheSizeBudget = 100L * 1024 * 1024;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(14);
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/webp", "image/jpeg", "image/png"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppPaths _paths;
    private readonly AppConfig _config;
    private readonly ILogger<CoverImageService> _logger;
    private readonly string _cacheDirectory;

    public CoverImageService(IHttpClientFactory httpClientFactory, IAppPaths paths, AppConfig config, ILogger<CoverImageService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _paths = paths;
        _config = config;
        _logger = logger;
        _cacheDirectory = Path.Combine(paths.CacheDirectory, "covers");
    }

    public async Task<string?> GetCachedCoverAsync(Uri coverUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coverUri);
        if (coverUri.Scheme != Uri.UriSchemeHttps || !_config.TrustedHosts.Contains(coverUri.Host))
        {
            _logger.LogWarning("Cover URL rejected (not HTTPS or not allowlisted): {Url}", coverUri);
            return null;
        }

        Directory.CreateDirectory(_cacheDirectory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(coverUri.AbsoluteUri))).ToLowerInvariant();
        var extension = Path.GetExtension(coverUri.AbsolutePath).ToLowerInvariant() switch
        {
            ".webp" => ".webp",
            ".jpg" or ".jpeg" => ".jpg",
            ".png" => ".png",
            _ => ".img"
        };
        var destination = Path.Combine(_cacheDirectory, key + extension);

        try
        {
            if (File.Exists(destination) && DateTime.UtcNow - File.GetLastWriteTimeUtc(destination) < Retention)
                return destination;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Cover cache lookup failed for {Url}", coverUri);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, coverUri);
            var client = _httpClientFactory.CreateClient("covers");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > MaximumImageBytes) return null;
            if (response.Content.Headers.ContentType?.MediaType is { } mediaType && !AllowedContentTypes.Contains(mediaType)) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var tempPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                    if (total > MaximumImageBytes) throw new ModHubException("Cover image exceeded the size limit.");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(tempPath, destination, true);
            return destination;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ModHubException)
        {
            try { foreach (var leftover in Directory.EnumerateFiles(_cacheDirectory, "*.tmp")) File.Delete(leftover); } catch (IOException) { }
            _logger.LogWarning(ex, "Cover download failed for {Url}", coverUri);
            return null;
        }
    }

    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory)) return;
                long total = 0;
                var files = Directory.EnumerateFiles(_cacheDirectory).Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc).ToList();
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow - file.LastWriteTimeUtc > Retention || total + file.Length > CacheSizeBudget)
                    {
                        file.Delete();
                    }
                    else
                    {
                        total += file.Length;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Cover cache cleanup failed");
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
