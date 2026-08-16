namespace ACModHub.Infrastructure.Services;

/// <summary>
/// Non-blocking snapshot of the user's concurrent-download setting. The app updates it at
/// startup and whenever settings are saved, so the download manager never blocks on I/O.
/// </summary>
public sealed class ConcurrencySettings
{
    public int ConcurrentDownloads { get; set; } = 3;
}
