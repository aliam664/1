using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ACModHub.Core.Models;

namespace ACModHub.UI.Services;

public interface IHtmlCatalogPageBuilder
{
    string Build(CatalogLoadResult result, string language);
}

public sealed class HtmlCatalogPageBuilder : IHtmlCatalogPageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public string Build(CatalogLoadResult result, string language)
    {
        ArgumentNullException.ThrowIfNull(result);
        var template = ReadResource("index.html");
        var css = ReadResource("catalog.css");
        var script = ReadResource("catalog.js");
        var catalogJson = SafeScriptJson(JsonSerializer.Serialize(result.Catalog, JsonOptions));
        var sourceJson = SafeScriptJson(JsonSerializer.Serialize(result.Source, JsonOptions));
        var warningJson = SafeScriptJson(JsonSerializer.Serialize(result.Warning, JsonOptions));
        var isPersian = language.StartsWith("fa", StringComparison.OrdinalIgnoreCase);
        return template
            .Replace("{{CSS}}", css, StringComparison.Ordinal)
            .Replace("{{JS}}", script, StringComparison.Ordinal)
            .Replace("{{CATALOG_JSON}}", catalogJson, StringComparison.Ordinal)
            .Replace("{{SOURCE_JSON}}", sourceJson, StringComparison.Ordinal)
            .Replace("{{WARNING_JSON}}", warningJson, StringComparison.Ordinal)
            .Replace("{{LANG}}", isPersian ? "fa-IR" : "en-US", StringComparison.Ordinal)
            .Replace("{{DIR}}", isPersian ? "rtl" : "ltr", StringComparison.Ordinal);
    }

    private static string SafeScriptJson(string json) => json.Replace("</", "<\\/", StringComparison.Ordinal).Replace("\u2028", "\\u2028", StringComparison.Ordinal).Replace("\u2029", "\\u2029", StringComparison.Ordinal);

    private static string ReadResource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("Resources.Catalog." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded catalog UI resource is missing: {fileName}");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
