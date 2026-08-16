using System.Net;
using System.Text.Json;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

/// <summary>
/// Checks the GitHub Releases feed of the launcher repository for a newer version.
/// Stable channel excludes prereleases; Beta includes them. Drafts are always excluded.
/// Downgrades are never offered (only versions greater than the running one qualify).
/// Metadata only — this service never downloads or installs anything.
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private const int MaximumReleases = 30;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppPaths _paths;
    private readonly AppConfig _config;
    private readonly ILogger<GitHubUpdateChecker> _logger;

    public GitHubUpdateChecker(IHttpClientFactory httpClientFactory, IAppPaths paths, AppConfig config, ILogger<GitHubUpdateChecker> logger)
    {
        _httpClientFactory = httpClientFactory;
        _paths = paths;
        _config = config;
        _logger = logger;
    }

    public async Task<UpdateCheckResult> CheckAsync(UpdateChannel channel, bool forceRefresh, CancellationToken cancellationToken = default)
    {
        var current = SemanticVersion.Parse(AppInfo.Version);
        var feedUri = new Uri($"https://api.github.com/repos/{_config.UpdateRepository}/releases?per_page={MaximumReleases}");

        var cacheDirectory = Path.Combine(_paths.CacheDirectory, "updates");
        var cachePath = Path.Combine(cacheDirectory, "releases.json");
        var etagPath = cachePath + ".etag";

        List<GitHubReleaseWire>? releases = null;
        try
        {
            var client = _httpClientFactory.CreateClient("updates");
            var requestUri = feedUri;
            var skipEtag = false;
            for (var hop = 0; hop <= _config.MaximumRedirects; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                if (hop == 0 && !forceRefresh && !skipEtag)
                {
                    var cachedEtag = ReadEtag(etagPath);
                    if (cachedEtag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", cachedEtag);
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    var cached = ReadCached(cachePath);
                    if (cached is not null) { releases = cached; break; }
                    // No cache to serve: retry once without the ETag header.
                    skipEtag = true;
                    continue;
                }
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    if (location is null) throw new ModHubException("The update feed sent a redirect without a Location header.");
                    if (!location.IsAbsoluteUri) location = new Uri(requestUri, location);
                    if (location.Scheme != Uri.UriSchemeHttps) throw new ModHubException("The update feed attempted a non-HTTPS redirect.");
                    requestUri = location;
                    continue;
                }
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var payload = await JsonSerializer.DeserializeAsync<List<GitHubReleaseWire>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new ModHubException("The update feed returned an empty response.");
                releases = payload;
                await WriteCacheAsync(cacheDirectory, cachePath, etagPath, payload, response.Headers.ETag?.ToString(), cancellationToken).ConfigureAwait(false);
                break;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or ModHubException)
        {
            _logger.LogWarning(ex, "Update check against {Endpoint} failed; falling back to cached feed", feedUri);
            releases = ReadCached(cachePath);
            if (releases is null) throw new ModHubException("The update check failed and no cached release feed is available.", ex);
        }

        var release = SelectRelease(releases, channel, current);
        if (release is null) return new UpdateCheckResult(false, current);

        return new UpdateCheckResult(true, current, release, release.IsPrerelease);
    }

    private static UpdateReleaseInfo? SelectRelease(List<GitHubReleaseWire> releases, UpdateChannel channel, SemanticVersion current)
    {
        SemanticVersion? bestVersion = null;
        GitHubReleaseWire? best = null;
        foreach (var release in releases)
        {
            if (release.Draft) continue;
            if (channel == UpdateChannel.Stable && release.Prerelease) continue;
            if (!SemanticVersion.TryParse(release.TagName, out var version)) continue;
            // Downgrade / same-version protection: only strictly newer releases qualify.
            if (version <= current) continue;
            if (bestVersion is not null && version < bestVersion.Value) continue;
            bestVersion = version;
            best = release;
        }
        if (best is null || bestVersion is null) return null;

        var assets = best.Assets
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.BrowserDownloadUrl))
            .Select(x => new UpdateAsset(x.Name!, x.Size, new Uri(x.BrowserDownloadUrl!)))
            .ToArray();
        return new UpdateReleaseInfo
        {
            Version = bestVersion.Value,
            Tag = best.TagName!,
            Title = string.IsNullOrWhiteSpace(best.Name) ? best.TagName! : best.Name,
            ReleaseNotes = best.Body ?? string.Empty,
            PublishedAt = best.PublishedAt ?? DateTimeOffset.UtcNow,
            HtmlUrl = new Uri(best.HtmlUrl ?? $"https://github.com/{AppConfig.Production.UpdateRepository}/releases/tag/{best.TagName}"),
            Assets = assets,
            IsPrerelease = best.Prerelease
        };
    }

    private static List<GitHubReleaseWire>? ReadCached(string cachePath)
    {
        try
        {
            if (!File.Exists(cachePath)) return null;
            return JsonSerializer.Deserialize<List<GitHubReleaseWire>>(File.ReadAllText(cachePath), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static string? ReadEtag(string etagPath)
    {
        try { return File.Exists(etagPath) ? File.ReadAllText(etagPath).Trim() : null; }
        catch (IOException) { return null; }
    }

    private static async Task WriteCacheAsync(string directory, string cachePath, string etagPath, List<GitHubReleaseWire> releases, string? etag, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var payload = JsonSerializer.Serialize(releases, JsonOptions);
        var tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tempPath, payload, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, cachePath, true);
        if (!string.IsNullOrWhiteSpace(etag))
        {
            await File.WriteAllTextAsync(etagPath + ".tmp", etag, cancellationToken).ConfigureAwait(false);
            File.Move(etagPath + ".tmp", etagPath, true);
        }
        else if (File.Exists(etagPath))
        {
            File.Delete(etagPath);
        }
    }

    private sealed class GitHubReleaseWire
    {
        public string? TagName { get; set; }
        public bool Prerelease { get; set; }
        public bool Draft { get; set; }
        public string? Name { get; set; }
        public string? Body { get; set; }
        public string? HtmlUrl { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public List<GitHubAssetWire> Assets { get; set; } = [];
    }

    private sealed class GitHubAssetWire
    {
        public string? Name { get; set; }
        public long Size { get; set; }
        public string? BrowserDownloadUrl { get; set; }
    }
}
