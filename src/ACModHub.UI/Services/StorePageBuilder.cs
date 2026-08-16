using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ACModHub.Core.Models;
using ACModHub.Infrastructure.Services;

namespace ACModHub.UI.Services;

public interface IStorePageBuilder
{
    /// <summary>Materializes the store page (index.html with inlined, safely-escaped data) into the cache directory.</summary>
    string Build(ModCatalog catalog, string language, IReadOnlyDictionary<string, string> strings, string sourceText, string warningText);
}

/// <summary>
/// Builds the WebView2 store page. All remote data is serialized to JSON, escaped against
/// script injection (</, U+2028/29, &lt; &gt; &amp;), and rendered by the JavaScript with
/// textContent only — remote data never becomes executable script or innerHTML.
/// </summary>
public sealed class StorePageBuilder : IStorePageBuilder
{
    private readonly IAppPaths _paths;

    public StorePageBuilder(IAppPaths paths) => _paths = paths;

    public string Build(ModCatalog catalog, string language, IReadOnlyDictionary<string, string> strings, string sourceText, string warningText)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var directory = Path.Combine(_paths.CacheDirectory, "store");
        Directory.CreateDirectory(directory);

        var template = ReadResource("index.html");
        var css = ReadResource("catalog.css");
        var script = ReadResource("catalog.js");
        var isPersian = language.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

        var payload = new StorePayload
        {
            Lang = isPersian ? "fa" : "en",
            Dir = isPersian ? "rtl" : "ltr",
            Source = sourceText,
            Warning = warningText,
            Strings = strings,
            Mods = catalog.Mods.Where(x => x.Status is CatalogModStatus.Published or CatalogModStatus.Deprecated or CatalogModStatus.Revoked).Select(x => new StoreModPayload
            {
                Id = x.Id,
                Name = x.Name.Get(language),
                Author = x.AuthorName,
                Version = x.Version,
                Category = x.Category.ToString().ToLowerInvariant(),
                Description = x.Description?.Get(language) ?? string.Empty,
                Tags = x.Tags,
                CoverUrl = x.CoverUri?.AbsoluteUri,
                Size = x.ExpectedSize,
                Sha256 = null, // never exposed to the page; downloads are handled by the trusted bridge
                PublishedAt = x.PublishedAt?.UtcDateTime,
                Status = x.Status.ToString().ToLowerInvariant(),
                BlockReason = x.BlockReason,
                Installable = x.IsInstallable
            }).ToList()
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var safeJson = EscapeForScript(json);

        var html = template
            .Replace("{{CSS}}", css, StringComparison.Ordinal)
            .Replace("{{JS}}", script, StringComparison.Ordinal)
            .Replace("{{DATA}}", safeJson, StringComparison.Ordinal);

        var indexPath = Path.Combine(directory, "index.html");
        var tempPath = indexPath + ".tmp";
        File.WriteAllText(tempPath, html, System.Text.Encoding.UTF8);
        File.Move(tempPath, indexPath, true);
        return indexPath;
    }

    /// <summary>Escapes a JSON string for safe inline placement inside a script element.</summary>
    internal static string EscapeForScript(string json) =>
        json.Replace("</", "<\\/", StringComparison.Ordinal)
           .Replace("\u2028", "\\u2028", StringComparison.Ordinal)
           .Replace("\u2029", "\\u2029", StringComparison.Ordinal)
           .Replace("<", "\\u003c", StringComparison.Ordinal)
           .Replace(">", "\\u003e", StringComparison.Ordinal)
           .Replace("&", "\\u0026", StringComparison.Ordinal);

    private static string ReadResource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("Resources.Catalog." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded catalog UI resource is missing: {fileName}");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class StorePayload
    {
        public string Lang { get; init; } = "en";
        public string Dir { get; init; } = "ltr";
        public string Source { get; init; } = string.Empty;
        public string Warning { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, string> Strings { get; init; } = new Dictionary<string, string>();
        public List<StoreModPayload> Mods { get; init; } = [];
    }

    private sealed class StoreModPayload
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public List<string> Tags { get; init; } = [];
        public string? CoverUrl { get; init; }
        public long? Size { get; init; }
        public string? Sha256 { get; init; }
        public DateTime? PublishedAt { get; init; }
        public string Status { get; init; } = "published";
        public string? BlockReason { get; init; }
        public bool Installable { get; init; }
    }
}
