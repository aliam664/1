using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using Microsoft.Extensions.Logging;

namespace ACModHub.Infrastructure.Services;

public sealed partial class JsonModCatalogService : IModCatalogService
{
    private const int MaximumCatalogBytes = 2 * 1024 * 1024;
    private const string EmbeddedResourceName = "ACModHub.Catalog.catalog.v1.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsService _settings;
    private readonly IAppPaths _paths;
    private readonly ILogger<JsonModCatalogService> _logger;

    public JsonModCatalogService(IHttpClientFactory httpClientFactory, ISettingsService settings, IAppPaths paths, ILogger<JsonModCatalogService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _paths = paths;
        _logger = logger;
    }

    public async Task<CatalogLoadResult> LoadAsync(bool forceRemoteRefresh = false, CancellationToken cancellationToken = default)
    {
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var cachePath = Path.Combine(_paths.CacheDirectory, "catalog", "catalog.v1.json");
        if (!string.IsNullOrWhiteSpace(settings.CatalogUrl))
        {
            try
            {
                var remoteUri = ValidateRemoteCatalogUri(settings.CatalogUrl);
                var json = await DownloadCatalogAsync(remoteUri, cancellationToken).ConfigureAwait(false);
                var catalog = ParseAndValidate(json);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                await File.WriteAllTextAsync(cachePath, json, cancellationToken).ConfigureAwait(false);
                return new(catalog, remoteUri.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Remote mod catalog could not be loaded; falling back to cache or embedded catalog");
                if (File.Exists(cachePath))
                {
                    try
                    {
                        var cached = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
                        return new(ParseAndValidate(cached), "cache", "Remote catalog unavailable; showing the last valid cached copy.");
                    }
                    catch (Exception cacheError) when (cacheError is IOException or JsonException or ModHubException)
                    {
                        _logger.LogWarning(cacheError, "Cached mod catalog is invalid");
                    }
                }
                var embeddedFallback = await ReadEmbeddedAsync(cancellationToken).ConfigureAwait(false);
                return new(ParseAndValidate(embeddedFallback), "embedded", "Remote catalog unavailable; showing the embedded catalog.");
            }
        }

        var embedded = await ReadEmbeddedAsync(cancellationToken).ConfigureAwait(false);
        return new(ParseAndValidate(embedded), "embedded");
    }

    private async Task<string> DownloadCatalogAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClientFactory.CreateClient("catalog").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumCatalogBytes)
            throw new ModHubException("The remote catalog exceeds the 2 MiB safety limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > MaximumCatalogBytes) throw new ModHubException("The remote catalog exceeded its read limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    private static ModCatalog ParseAndValidate(string json)
    {
        var catalog = JsonSerializer.Deserialize<ModCatalog>(json, JsonOptions) ?? throw new ModHubException("Catalog JSON is empty.");
        if (catalog.SchemaVersion != 1) throw new ModHubException($"Unsupported catalog schema version: {catalog.SchemaVersion}.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in catalog.Mods)
        {
            if (string.IsNullOrWhiteSpace(mod.Id) || !SafeIdRegex().IsMatch(mod.Id)) throw new ModHubException($"Catalog mod ID is invalid: {mod.Id}");
            if (!ids.Add(mod.Id)) throw new ModHubException($"Duplicate catalog mod ID: {mod.Id}");
            if (string.IsNullOrWhiteSpace(mod.Name)) throw new ModHubException($"Catalog mod '{mod.Id}' has no name.");
            if (mod.DownloadUrl is not null && mod.DownloadUrl.Scheme is not ("https" or "http")) throw new ModHubException($"Catalog mod '{mod.Id}' has a non-HTTP download URL.");
            if (mod.ThumbnailUrl is not null && mod.ThumbnailUrl.Scheme is not ("https" or "http")) throw new ModHubException($"Catalog mod '{mod.Id}' has an invalid thumbnail URL.");
            if (!string.IsNullOrWhiteSpace(mod.FileName) && Path.GetFileName(mod.FileName) != mod.FileName) throw new ModHubException($"Catalog mod '{mod.Id}' has an unsafe file name.");
            if (mod.DownloadUrl is not null)
            {
                var packageName = string.IsNullOrWhiteSpace(mod.FileName) ? Path.GetFileName(mod.DownloadUrl.LocalPath) : mod.FileName;
                var extension = Path.GetExtension(packageName).ToLowerInvariant();
                if (extension is not (".zip" or ".7z" or ".rar")) throw new ModHubException($"Catalog mod '{mod.Id}' must point to a ZIP, 7Z, or RAR package.");
            }
            if (!string.IsNullOrWhiteSpace(mod.Sha256) && (mod.Sha256.Length != 64 || mod.Sha256.Any(x => !Uri.IsHexDigit(x)))) throw new ModHubException($"Catalog mod '{mod.Id}' has an invalid SHA-256.");
            if (mod.ExpectedSize is <= 0) throw new ModHubException($"Catalog mod '{mod.Id}' has an invalid expected size.");
        }
        return catalog;
    }

    private static Uri ValidateRemoteCatalogUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ModHubException("Remote catalog URL must be an absolute HTTPS URL.");
        return uri;
    }

    private static async Task<string> ReadEmbeddedAsync(CancellationToken cancellationToken)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new ModHubException("Embedded catalog resource is missing.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._-]{1,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdRegex();
}
