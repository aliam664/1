using ACModHub.Core.Models;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class SettingsValidationTests
{
    [Fact]
    public async Task Defaults_ArePersianStableAndSafe()
    {
        using var environment = new TestEnvironment();
        var settings = await environment.Get<ACModHub.Core.Interfaces.ISettingsService>().LoadAsync();
        Assert.Equal("fa-IR", settings.Language);
        Assert.Equal("stable", settings.UpdateChannel);
        Assert.True(settings.CheckForUpdatesOnStartup);
        Assert.Equal(3, settings.ConcurrentDownloads);
        Assert.Null(settings.CatalogUrl);
    }

    [Fact]
    public async Task ConcurrentDownloads_IsClamped()
    {
        using var environment = new TestEnvironment();
        var service = environment.Get<ACModHub.Core.Interfaces.ISettingsService>();
        var settings = await service.LoadAsync();
        settings.ConcurrentDownloads = 1000;
        await service.SaveAsync(settings);
        var loaded = await service.LoadAsync();
        Assert.Equal(8, loaded.ConcurrentDownloads);
    }

    [Fact]
    public async Task UpdateChannel_IsNormalized()
    {
        using var environment = new TestEnvironment();
        var service = environment.Get<ACModHub.Core.Interfaces.ISettingsService>();
        var settings = await service.LoadAsync();
        settings.UpdateChannel = "nightly";
        await service.SaveAsync(settings);
        Assert.Equal("stable", (await service.LoadAsync()).UpdateChannel);
        settings.UpdateChannel = "beta";
        await service.SaveAsync(settings);
        Assert.Equal("beta", (await service.LoadAsync()).UpdateChannel);
    }

    [Fact]
    public async Task Language_IsNormalized()
    {
        using var environment = new TestEnvironment();
        var service = environment.Get<ACModHub.Core.Interfaces.ISettingsService>();
        var settings = await service.LoadAsync();
        settings.Language = "fr";
        await service.SaveAsync(settings);
        Assert.Equal("en-US", (await service.LoadAsync()).Language);
    }

    [Fact]
    public async Task RelativeGamePath_IsRejected()
    {
        using var environment = new TestEnvironment();
        var service = environment.Get<ACModHub.Core.Interfaces.ISettingsService>();
        var settings = await service.LoadAsync();
        settings.GamePath = "assettocorsa";
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.SaveAsync(settings));
    }

    [Fact]
    public void StaticValidation_RejectsRelativePath()
    {
        var settings = new AppSettings { GamePath = "relative/path" };
        Assert.Throws<ArgumentException>(() => AppSettings.Validate(settings));
    }

    [Fact]
    public void StaticValidation_KeepsValidHttpsOverride()
    {
        var settings = new AppSettings { CatalogUrl = "https://example.com/catalog.v1.json" };
        var validated = AppSettings.Validate(settings);
        Assert.Equal("https://example.com/catalog.v1.json", validated.CatalogUrl);
    }
}
