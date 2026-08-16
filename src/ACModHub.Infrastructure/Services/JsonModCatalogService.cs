using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

/// <summary>
/// Catalog client for the public aliam664/Data catalog.
///
/// Endpoint chain: advanced HTTPS override (user settings) → GitHub Pages primary
/// → raw generated fallback → embedded empty catalog.
/// The cache is only ever replaced by a fully validated payload (atomic write), so a
/// corrupt or malicious response can never destroy the last-known-good copy.
/// </summary>
public sealed partial class JsonModCatalogService : IModCatalogService
{
    private const string EmbeddedResourceName = "ACModHub.Catalog.catalog.v1.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsService _settings;
    private readonly IAppPaths _paths;
    private readonly AppConfig _config;
    private readonly ILogger<JsonModCatalogService> _logger;

    public JsonModCatalogService(IHttpClientFactory httpClientFactory, ISettingsService settings, IAppPaths paths, AppConfig config, ILogger<JsonModCatalogService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _paths = paths;
        _config = config;
        _logger = logger;
    }

    public async Task<CatalogLoadResult> LoadAsync(bool forceRemoteRefresh = false, CancellationToken cancellationToken = default)
    {
        var cacheDirectory = Path.Combine(_paths.CacheDirectory, "catalog");
        var cachePath = Path.Combine(cacheDirectory, "catalog.v1.json");
        var etagPath = cachePath + ".etag";

        // Load cache first so it can satisfy 304 and network failures instantly.
        var cached = await TryReadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
        var etag = ReadEtag(etagPath);

        var settings = AppSettings.Validate(await _settings.LoadAsync(cancellationToken).ConfigureAwait(false));
        var endpointCandidates = BuildEndpointCandidates(settings);

        Exception? lastFailure = null;
        foreach (var endpoint in endpointCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ifNoneMatch = forceRemoteRefresh ? null : etag;
            try
            {
                var remote = await FetchAsync(endpoint, ifNoneMatch, cancellationToken).ConfigureAwait(false);
                if (remote is null)
                {
                    // 304 Not Modified: the cached copy is authoritative.
                    if (cached is not null)
                        return new(cached.Value.Catalog, "cache", IsCached: true, Warning: cached.Value.Warning);
                    continue; // no cache yet — try the next endpoint with a full request.
                }

                var (validated, catalogWarning) = ParseAndValidate(remote.Payload, remote.DocumentUri);
                await WriteCacheAsync(cacheDirectory, cachePath, etagPath, remote.Payload, remote.Etag, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Mod catalog loaded from {Endpoint} ({ModCount} mods)", remote.DocumentUri, validated.Mods.Count);
                return new(validated, SourceName(endpoint), Warning: catalogWarning);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or ModHubException or ArgumentException)
            {
                lastFailure = ex;
                _logger.LogWarning(ex, "Catalog endpoint {Endpoint} failed; trying the next source", endpoint);
            }
        }

        // All remote endpoints failed: fall back to the last-known-good cache.
        if (cached is not null)
        {
            _logger.LogWarning(lastFailure, "Using the last-known-good cached catalog");
            return new(cached.Value.Catalog, "cache", IsCached: true, Warning: CatalogWarning.RemoteUnavailableUsingCache);
        }

        _logger.LogWarning(lastFailure, "No valid cached catalog; using the embedded empty catalog");
        var (embedded, _) = ParseAndValidate(await ReadEmbeddedAsync(cancellationToken).ConfigureAwait(false), null);
        return new(embedded, "embedded", Warning: CatalogWarning.RemoteUnavailableUsingEmbedded);
    }

    // ---------------------------------------------------------------- endpoint chain

    private List<Uri> BuildEndpointCandidates(AppSettings settings)
    {
        var candidates = new List<Uri>(3);
        if (!string.IsNullOrWhiteSpace(settings.CatalogUrl))
        {
            if (Uri.TryCreate(settings.CatalogUrl, UriKind.Absolute, out var overrideUri) && overrideUri.Scheme == Uri.UriSchemeHttps)
            {
                candidates.Add(overrideUri);
            }
            else
            {
                _logger.LogWarning("Ignoring catalog override: it must be an absolute HTTPS URL");
            }
        }
        candidates.Add(_config.CatalogPrimaryUrl);
        candidates.Add(_config.CatalogFallbackUrl);
        return candidates.Distinct().ToList();
    }

    private static string SourceName(Uri endpoint) =>
        endpoint.Host.Contains("github.io", StringComparison.OrdinalIgnoreCase) ? "primary"
        : endpoint.Host.Contains("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ? "fallback"
        : "override";

    // ---------------------------------------------------------------- HTTP + redirects

    private sealed record RemotePayload(string Payload, Uri DocumentUri, string? Etag);

    private async Task<RemotePayload?> FetchAsync(Uri uri, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("catalog");
        var current = uri;
        for (var hop = 0; hop <= _config.MaximumRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            if (hop == 0 && ifNoneMatch is not null) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified) return null;
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                if (location is null) throw new ModHubException("The catalog server sent a redirect without a Location header.");
                // Redirects must remain HTTPS — an HTTP hop is a downgrade attack vector.
                if (!location.IsAbsoluteUri)
                {
                    location = new Uri(current, location);
                }
                if (location.Scheme != Uri.UriSchemeHttps) throw new ModHubException("The catalog server attempted to redirect to a non-HTTPS URL.");
                current = location;
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > _config.CatalogMaximumBytes)
                throw new ModHubException("The remote catalog exceeds the size safety limit.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (memory.Length + read > _config.CatalogMaximumBytes)
                    throw new ModHubException("The remote catalog exceeded its read limit while streaming.");
                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            var etag = response.Headers.ETag?.ToString();
            return new RemotePayload(System.Text.Encoding.UTF8.GetString(memory.ToArray()), current, etag);
        }
        throw new ModHubException("The catalog server redirected too many times.");
    }

    // ---------------------------------------------------------------- cache (atomic, last-known-good)

    private async Task<(ModCatalog Catalog, string Payload, CatalogWarning Warning)?> TryReadCacheAsync(string cachePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath)) return null;
        try
        {
            var payload = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
            var (catalog, warning) = ParseAndValidate(payload, null);
            return (catalog, payload, warning);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ModHubException)
        {
            _logger.LogWarning(ex, "Cached catalog is invalid and will be ignored");
            return null;
        }
    }

    private static string? ReadEtag(string etagPath)
    {
        try { return File.Exists(etagPath) ? File.ReadAllText(etagPath).Trim() : null; }
        catch (IOException) { return null; }
    }

    private static async Task WriteCacheAsync(string directory, string cachePath, string etagPath, string payload, string? etag, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tempPath, payload, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, cachePath, true);
        if (!string.IsNullOrWhiteSpace(etag))
        {
            var etagTemp = etagPath + ".tmp";
            await File.WriteAllTextAsync(etagTemp, etag, cancellationToken).ConfigureAwait(false);
            File.Move(etagTemp, etagPath, true);
        }
        else if (File.Exists(etagPath))
        {
            File.Delete(etagPath);
        }
    }

    // ---------------------------------------------------------------- validation

    private (ModCatalog Catalog, CatalogWarning Warning) ParseAndValidate(string json, Uri? documentUri)
    {
        var wire = JsonSerializer.Deserialize<CatalogWire>(json, JsonOptions)
            ?? throw new ModHubException("Catalog JSON is empty.");

        if (wire.SchemaVersion != 1)
            throw new ModHubException($"Unsupported catalog schema version: {wire.SchemaVersion}.");
        if (wire.ModList is null)
            throw new ModHubException("The catalog has no mods array.");
        if (wire.ModList.Count > _config.CatalogMaximumMods)
            throw new ModHubException($"The catalog exceeds the {_config.CatalogMaximumMods} mod safety limit.");

        var currentLauncher = SemanticVersion.TryParse(AppInfo.Version, out var parsedCurrent) ? parsedCurrent : (SemanticVersion?)null;
        var catalogMinimum = ParseMinimumVersion(wire.MinimumLauncherVersion);
        var warning = CatalogWarning.None;
        if (catalogMinimum is not null && currentLauncher is not null && currentLauncher.Value < catalogMinimum.Value)
        {
            _logger.LogWarning("Catalog requires launcher {Required} but the running launcher is {Current}", catalogMinimum, currentLauncher);
            warning = CatalogWarning.CatalogRequiresNewerLauncher;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mods = new List<CatalogMod>(wire.ModList.Count);
        foreach (var item in wire.ModList)
        {
            ValidateWireMod(item, ids, documentUri, currentLauncher, out var domain);
            if (domain is not null) mods.Add(domain);
        }

        var catalog = new ModCatalog
        {
            SchemaVersion = 1,
            Revision = wire.Revision is > 0 ? wire.Revision : 1,
            GeneratedAt = wire.GeneratedAt,
            Repository = string.IsNullOrWhiteSpace(wire.Repository) ? "aliam664/Data" : wire.Repository,
            MinimumLauncherVersion = catalogMinimum?.ToString(),
            Mods = mods
        };
        return (catalog, warning);
    }

    private void ValidateWireMod(CatalogWireMod item, HashSet<string> ids, Uri? documentUri, SemanticVersion? currentLauncher, out CatalogMod? domain)
    {
        domain = null;

        if (string.IsNullOrWhiteSpace(item.Id) || !SafeIdRegex().IsMatch(item.Id) || item.Id.Length > 80)
            throw new ModHubException($"Catalog mod ID is invalid: {item.Id}");
        if (!ids.Add(item.Id))
            throw new ModHubException($"Duplicate catalog mod ID: {item.Id}");

        // draft and hidden are excluded from the public output entirely.
        if (item.Status is CatalogWireStatus.Draft or CatalogWireStatus.Hidden)
            return;

        var name = item.Name ?? throw new ModHubException($"Catalog mod '{item.Id}' has no name.");
        if (string.IsNullOrWhiteSpace(name.Fa) && string.IsNullOrWhiteSpace(name.En))
            throw new ModHubException($"Catalog mod '{item.Id}' has an empty name.");
        if ((name.Fa?.Length ?? 0) > 120 || (name.En?.Length ?? 0) > 120)
            throw new ModHubException($"Catalog mod '{item.Id}' has a name longer than 120 characters.");
        if ((item.Author?.Name?.Length ?? 0) > 80)
            throw new ModHubException($"Catalog mod '{item.Id}' has an author name longer than 80 characters.");
        if (!string.IsNullOrWhiteSpace(item.Author?.Url)
            && (!Uri.TryCreate(item.Author.Url, UriKind.Absolute, out var authorUri) || authorUri.Scheme != Uri.UriSchemeHttps))
            throw new ModHubException($"Catalog mod '{item.Id}' has an author URL that is not HTTPS.");
        if (!SemanticVersion.TryParse(item.Version, out _))
            throw new ModHubException($"Catalog mod '{item.Id}' has an invalid version: {item.Version}");
        if (item.Category is null)
            throw new ModHubException($"Catalog mod '{item.Id}' has no category.");

        var description = item.Description;
        if (description is not null)
        {
            foreach (var text in new[] { description.Fa, description.En })
            {
                if (text is null) continue;
                if (text.Length > 2000) throw new ModHubException($"Catalog mod '{item.Id}' has a description longer than 2000 characters.");
                if (text.Any(char.IsControl)) throw new ModHubException($"Catalog mod '{item.Id}' has a description with control characters.");
            }
        }

        if (item.Tags is not null)
        {
            if (item.Tags.Count > 32) throw new ModHubException($"Catalog mod '{item.Id}' has more than 32 tags.");
            if (item.Tags.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 64)) throw new ModHubException($"Catalog mod '{item.Id}' has an invalid tag.");
        }

        // Package validation: only https download URLs from the release repository are accepted.
        Uri? downloadUrl = null;
        string? fileName = null;
        if (item.Status is CatalogWireStatus.Published or CatalogWireStatus.Deprecated)
        {
            var package = item.Package ?? throw new ModHubException($"Catalog mod '{item.Id}' has no package.");
            if (string.IsNullOrWhiteSpace(package.DownloadUrl)
                || !Uri.TryCreate(package.DownloadUrl, UriKind.Absolute, out downloadUrl)
                || downloadUrl.Scheme != Uri.UriSchemeHttps
                || !_config.TrustedHosts.Contains(downloadUrl.Host))
                throw new ModHubException($"Catalog mod '{item.Id}' has an invalid package download URL.");
            if (downloadUrl.AbsoluteUri.Length > 2048)
                throw new ModHubException($"Catalog mod '{item.Id}' has a download URL longer than 2048 characters.");

            fileName = string.IsNullOrWhiteSpace(package.AssetName) ? Path.GetFileName(downloadUrl.LocalPath) : package.AssetName;
            if (string.IsNullOrWhiteSpace(fileName) || !SafePath.IsSafeFileName(fileName))
                throw new ModHubException($"Catalog mod '{item.Id}' has an unsafe package file name: {fileName}");
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension is not (".zip" or ".7z" or ".rar"))
                throw new ModHubException($"Catalog mod '{item.Id}' must point to a ZIP, 7Z, or RAR package.");

            if (package.Size is <= 0)
                throw new ModHubException($"Catalog mod '{item.Id}' has an invalid package size.");
            if (!string.IsNullOrWhiteSpace(package.Sha256) && (package.Sha256.Length != 64 || package.Sha256.Any(x => !Uri.IsHexDigit(x))))
                throw new ModHubException($"Catalog mod '{item.Id}' has an invalid package SHA-256.");
        }

        // Cover: a safe relative path resolved against the catalog document base, HTTPS only.
        Uri? coverUrl = null;
        if (!string.IsNullOrWhiteSpace(item.Cover))
        {
            var normalized = item.Cover.Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Contains("../", StringComparison.Ordinal) || Path.IsPathRooted(normalized))
                throw new ModHubException($"Catalog mod '{item.Id}' has an unsafe cover path.");
            var baseUri = documentUri is not null ? documentUri : _config.CatalogPrimaryUrl;
            var candidate = new Uri(new Uri(baseUri, "."), normalized);
            if (candidate.Scheme != Uri.UriSchemeHttps || !_config.TrustedHosts.Contains(candidate.Host))
                throw new ModHubException($"Catalog mod '{item.Id}' has a cover URL outside the trusted hosts.");
            coverUrl = candidate;
        }

        // Status semantics.
        string? blockReason = null;
        if (item.Status == CatalogWireStatus.Revoked)
        {
            if (string.IsNullOrWhiteSpace(item.RevocationReason))
                throw new ModHubException($"Catalog mod '{item.Id}' is revoked without a revocation reason.");
            blockReason = item.RevocationReason;
        }
        else
        {
            var required = ParseMinimumVersion(item.MinimumLauncherVersion);
            if (required is not null && currentLauncher is not null && currentLauncher.Value < required.Value)
            {
                blockReason = $"requires-launcher:{required}";
            }
        }

        domain = new CatalogMod
        {
            Id = item.Id,
            Status = item.Status == CatalogWireStatus.Revoked ? CatalogModStatus.Revoked
                   : item.Status == CatalogWireStatus.Deprecated ? CatalogModStatus.Deprecated
                   : CatalogModStatus.Published,
            RevocationReason = item.RevocationReason,
            Name = new LocalizedText(name.Fa, name.En),
            AuthorName = item.Author?.Name ?? string.Empty,
            AuthorUrl = item.Author?.Url,
            Version = item.Version ?? "1.0.0",
            Category = MapCategory(item.Category.Value),
            Description = description is null ? null : new LocalizedText(description.Fa, description.En),
            Tags = item.Tags ?? [],
            DownloadUrl = downloadUrl,
            FileName = fileName,
            Sha256 = string.IsNullOrWhiteSpace(item.Package?.Sha256) ? null : item.Package.Sha256,
            ExpectedSize = item.Package?.Size,
            MinimumLauncherVersion = required?.ToString(),
            PublishedAt = item.PublishedAt,
            BlockReason = blockReason,
            CoverUri = coverUrl
        };
    }

    private static SemanticVersion? ParseMinimumVersion(string? value) =>
        SemanticVersion.TryParse(value, out var parsed) ? parsed : null;

    private static ModCategory MapCategory(CatalogWireCategory category) => category switch
    {
        CatalogWireCategory.Car => ModCategory.Car,
        CatalogWireCategory.Track => ModCategory.Track,
        CatalogWireCategory.Skin => ModCategory.Skin,
        CatalogWireCategory.App => ModCategory.App,
        CatalogWireCategory.Weather => ModCategory.Weather,
        CatalogWireCategory.Csp => ModCategory.Csp,
        _ => ModCategory.Miscellaneous
    };

    private static async Task<string> ReadEmbeddedAsync(CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new ModHubException("Embedded catalog resource is missing.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdRegex();

    // ---------------------------------------------------------------- wire DTOs (public catalog schema)

    private sealed class CatalogWire
    {
        public int SchemaVersion { get; set; } = 1;
        public long? Revision { get; set; }
        public DateTimeOffset? GeneratedAt { get; set; }
        public string? Repository { get; set; }
        public string? MinimumLauncherVersion { get; set; }
        [JsonPropertyName("mods")] public List<CatalogWireMod>? ModList { get; set; }
    }

    private sealed class CatalogWireMod
    {
        public string? Id { get; set; }
        public CatalogWireStatus Status { get; set; } = CatalogWireStatus.Published;
        public string? RevocationReason { get; set; }
        public CatalogWireLocalized? Name { get; set; }
        public CatalogWireAuthor? Author { get; set; }
        public string? Version { get; set; }
        public CatalogWireCategory? Category { get; set; }
        public CatalogWireLocalized? Description { get; set; }
        public string? Cover { get; set; }
        public List<string>? Tags { get; set; }
        public CatalogWirePackage? Package { get; set; }
        public string? MinimumLauncherVersion { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class CatalogWireLocalized
    {
        public string? Fa { get; set; }
        public string? En { get; set; }
    }

    private sealed class CatalogWireAuthor
    {
        public string? Name { get; set; }
        public string? Url { get; set; }
    }

    private sealed class CatalogWirePackage
    {
        public string? ReleaseTag { get; set; }
        public string? AssetName { get; set; }
        public string? DownloadUrl { get; set; }
        public long? Size { get; set; }
        public string? Sha256 { get; set; }
    }

    private enum CatalogWireStatus { Draft, Published, Hidden, Deprecated, Revoked }

    private enum CatalogWireCategory { Car, Track, Skin, App, Weather, Csp, Miscellaneous }
}
