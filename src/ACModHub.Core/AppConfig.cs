namespace ACModHub.Core;

/// <summary>
/// Central, strongly-typed application configuration. Endpoint defaults are defined here
/// exactly once; services and the UI read from this object instead of hard-coding URLs.
/// A user-supplied catalog override lives in <see cref="Models.AppSettings"/> (Advanced section)
/// and is always validated as absolute HTTPS.
/// </summary>
public sealed class AppConfig
{
    public static AppConfig Production { get; } = new();

    /// <summary>Primary catalog endpoint (GitHub Pages, enabled after the Data repo activates Pages).</summary>
    public Uri CatalogPrimaryUrl { get; init; } = new("https://aliam664.github.io/Data/catalog.v1.json");

    /// <summary>Raw generated fallback endpoint (generated branch of the Data repository).</summary>
    public Uri CatalogFallbackUrl { get; init; } = new("https://raw.githubusercontent.com/aliam664/Data/generated/catalog.v1.json");

    /// <summary>Update repository used for launcher updates (GitHub Releases API).</summary>
    public string UpdateRepository { get; init; } = "aliam664/1";

    /// <summary>Maximum accepted catalog payload before the response is treated as invalid.</summary>
    public int CatalogMaximumBytes { get; init; } = 2 * 1024 * 1024;

    /// <summary>Maximum number of mods accepted from a catalog (protects memory and UI).</summary>
    public int CatalogMaximumMods { get; init; } = 2000;

    /// <summary>Timeout for catalog and update-metadata requests.</summary>
    public TimeSpan MetadataTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Maximum redirect hops for catalog and update requests. Every hop must remain HTTPS.</summary>
    public int MaximumRedirects { get; init; } = 5;

    /// <summary>Default update channel.</summary>
    public string DefaultUpdateChannel { get; init; } = "stable";

    /// <summary>Update check runs in the background after the main window is shown.</summary>
    public bool CheckForUpdatesOnStartup { get; init; } = true;

    /// <summary>Update packages are never downloaded without explicit user consent.</summary>
    public bool AutomaticUpdateDownload { get; init; } = false;

    /// <summary>Update packages are never installed without explicit user consent.</summary>
    public bool AutomaticUpdateInstall { get; init; } = false;

    /// <summary>Hosts allowed to serve catalog covers and catalog package downloads.</summary>
    public IReadOnlySet<string> TrustedHosts { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "aliam664.github.io",
        "raw.githubusercontent.com",
        "objects.githubusercontent.com",
        "api.github.com"
    };
}
