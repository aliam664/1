using ACModHub.Core.Interfaces;
using ACModHub.Tests.TestSupport;

namespace ACModHub.Tests.Infrastructure;

public sealed class CatalogTests
{
    [Fact]
    public async Task EmbeddedCatalog_LoadsWithoutExternalNetwork()
    {
        using var environment = new TestEnvironment();
        var result = await environment.Get<IModCatalogService>().LoadAsync();
        Assert.Equal(1, result.Catalog.SchemaVersion);
        Assert.Equal("embedded", result.Source);
        Assert.Empty(result.Catalog.Mods);
    }

    [Fact]
    public async Task InvalidRemoteUrl_FallsBackToEmbeddedCatalog()
    {
        using var environment = new TestEnvironment();
        var settings = environment.Get<ISettingsService>();
        var model = await settings.LoadAsync();
        model.CatalogUrl = "http://insecure.example/catalog.json";
        await settings.SaveAsync(model);
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("embedded", result.Source);
        Assert.NotNull(result.Warning);
    }
}
