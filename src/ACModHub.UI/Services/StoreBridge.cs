using System.Text.Json;
using System.Text.RegularExpressions;
using ACModHub.Core;

namespace ACModHub.UI.Services;

public enum StoreBridgeCommand { InstallMod, RefreshCatalog, CancelOperation, OpenTrustedExternalLink }

/// <summary>
/// Schema validation for every message coming from the WebView2 store. The store can only
/// invoke these four commands; arguments are strictly validated before dispatch, and
/// external URLs must be HTTPS on the allowlist before they reach the shell.
/// </summary>
public static partial class StoreBridge
{
    public sealed record Message(StoreBridgeCommand Command, string? ModId = null, string? Url = null);

    public static bool TryParse(string json, out Message? message, out string? error)
    {
        message = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4096)
        {
            error = "Message is empty or too large.";
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Message must be a JSON object.";
                return false;
            }
            if (!root.TryGetProperty("command", out var commandElement) || commandElement.ValueKind != JsonValueKind.String)
            {
                error = "Message has no command.";
                return false;
            }
            var command = commandElement.GetString() switch
            {
                "installMod" => StoreBridgeCommand.InstallMod,
                "refreshCatalog" => StoreBridgeCommand.RefreshCatalog,
                "cancelOperation" => StoreBridgeCommand.CancelOperation,
                "openExternal" => StoreBridgeCommand.OpenTrustedExternalLink,
                _ => (StoreBridgeCommand?)null
            };
            if (command is null)
            {
                error = "Unknown command.";
                return false;
            }
            string? modId = null;
            string? url = null;
            if (command == StoreBridgeCommand.InstallMod)
            {
                if (!root.TryGetProperty("modId", out var modIdElement) || modIdElement.ValueKind != JsonValueKind.String)
                {
                    error = "installMod requires a modId.";
                    return false;
                }
                modId = modIdElement.GetString();
                if (string.IsNullOrWhiteSpace(modId) || modId.Length > 80 || !SafeIdRegex().IsMatch(modId))
                {
                    error = "modId is invalid.";
                    return false;
                }
            }
            if (command == StoreBridgeCommand.OpenTrustedExternalLink)
            {
                if (!root.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
                {
                    error = "openExternal requires a url.";
                    return false;
                }
                url = urlElement.GetString();
                if (!IsTrustedExternalUrl(url))
                {
                    error = "The URL is not trusted.";
                    return false;
                }
            }
            message = new Message(command.Value, modId, url);
            return true;
        }
        catch (JsonException)
        {
            error = "Message is not valid JSON.";
            return false;
        }
    }

    public static bool IsTrustedExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        var trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "github.com", "aliam664.github.io"
        };
        return trusted.Contains(uri.Host);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{1,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdRegex();
}
