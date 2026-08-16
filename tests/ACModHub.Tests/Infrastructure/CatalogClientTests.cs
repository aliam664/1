using System.Net;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ACModHub.Tests.Infrastructure;

public sealed class CatalogClientTests
{
    private const string EmptyCatalogJson = """
        {"schemaVersion":1,"revision":1,"generatedAt":"2026-08-16T00:00:00Z","repository":"aliam664/Data","minimumLauncherVersion":"2.0.0","mods":[]}
        """;

    private const string OneModJson = """
        {
          "schemaVersion": 1, "revision": 2, "repository": "aliam664/Data", "minimumLauncherVersion": "2.0.0",
          "mods": [
            {
              "id": "author.car-1",
              "status": "published",
              "name": { "fa": "ماشین تست", "en": "Test car" },
              "author": { "name": "Author", "url": "https://example.com" },
              "version": "1.2.3",
              "category": "car",
              "description": { "fa": "توضیح", "en": "desc" },
              "cover": "assets/covers/author.car-1.webp",
              "tags": ["tag1"],
              "package": {
                "releaseTag": "author.car-1-v1.2.3",
                "assetName": "author.car-1-v1.2.3.zip",
                "downloadUrl": "https://github.com/aliam664/Data/releases/download/author.car-1-v1.2.3/author.car-1-v1.2.3.zip",
                "size": 123456,
                "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
              },
              "publishedAt": "2026-08-01T00:00:00Z"
            }
          ]
        }
        """;

    private static TestEnvironment Environment(FakeCatalogHandler handler, AppConfig? config = null)
    {
        var environment = new TestEnvironment(services =>
        {
            services.AddHttpClient("catalog").ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddSingleton(config ?? new AppConfig
            {
                CatalogPrimaryUrl = new Uri("https://catalog.test/primary.json"),
                CatalogFallbackUrl = new Uri("https://catalog.test/fallback.json")
            });
        });
        return environment;
    }

    [Fact]
    public async Task ValidRemoteCatalog_IsParsedAndCached()
    {
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(OneModJson, "\"v1\""));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("primary", result.Source);
        Assert.Single(result.Catalog.Mods);
        Assert.Equal("author.car-1", result.Catalog.Mods[0].Id);
        Assert.NotNull(result.Catalog.Mods[0].CoverUri);
        Assert.True(result.Catalog.Mods[0].CoverUri!.IsAbsoluteUri);
    }

    [Fact]
    public async Task NotModified_AfterSeeding_ReturnsCache()
    {
        using var environment = Environment(new FakeCatalogHandler()
            .EnqueueJson(OneModJson, "\"etag-2\"")
            .Enqueue(HttpStatusCode.NotModified));
        var service = environment.Get<IModCatalogService>();
        var first = await service.LoadAsync(true);
        Assert.Equal("primary", first.Source);
        var second = await service.LoadAsync();
        Assert.Equal("cache", second.Source);
        Assert.True(second.IsCached);
    }

    [Fact]
    public async Task HttpDowngradeRedirect_IsRejected()
    {
        using var environment = Environment(new FakeCatalogHandler()
            .EnqueueRedirect("http://evil.example/catalog.json"));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("embedded", result.Source);
        Assert.Equal(CatalogWarning.RemoteUnavailableUsingEmbedded, result.Warning);
    }

    [Fact]
    public async Task HttpsRedirect_IsFollowed()
    {
        using var environment = Environment(new FakeCatalogHandler()
            .EnqueueRedirect("https://catalog.test/real.json")
            .EnqueueJson(OneModJson, "\"etag-3\""));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("primary", result.Source);
        Assert.Single(result.Catalog.Mods);
    }

    [Fact]
    public async Task InvalidPayload_DoesNotReplaceLastKnownGoodCache()
    {
        using var environment = Environment(new FakeCatalogHandler()
            .EnqueueJson(OneModJson, "\"etag-4\"")
            .EnqueueJson("""{"schemaVersion":1,"mods":[{"id":"broken"}]}""", "\"etag-5\""));
        var service = environment.Get<IModCatalogService>();
        var first = await service.LoadAsync(true);
        Assert.Equal("primary", first.Source);
        var second = await service.LoadAsync(true);
        // The invalid payload is rejected; the last-known-good cache is served.
        Assert.Equal("cache", second.Source);
        Assert.Single(second.Catalog.Mods);
    }

    [Fact]
    public async Task DuplicateIds_AreRejected()
    {
        var json = """{"schemaVersion":1,"mods":[{"id":"a.b","status":"published","name":{"en":"x"},"version":"1.0.0","category":"car","package":{"downloadUrl":"https://github.com/aliam664/Data/releases/download/t/a.zip","assetName":"a.zip","size":1}},{"id":"a.b","status":"published","name":{"en":"y"},"version":"1.0.0","category":"car","package":{"downloadUrl":"https://github.com/aliam664/Data/releases/download/t/a.zip","assetName":"a.zip","size":1}}]}""";
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(json));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("embedded", result.Source);
    }

    [Fact]
    public async Task DraftAndHidden_AreExcluded_DeprecatedKept_RevokedBlocked()
    {
        var json = """
        {"schemaVersion":1,"mods":[
          {"id":"a.draft","status":"draft","name":{"en":"d"},"version":"1.0.0","category":"car","package":{"downloadUrl":"https://github.com/aliam664/Data/releases/download/t/a.zip","assetName":"a.zip","size":1}},
          {"id":"a.hidden","status":"hidden","name":{"en":"h"},"version":"1.0.0","category":"car","package":{"downloadUrl":"https://github.com/aliam664/Data/releases/download/t/a.zip","assetName":"a.zip","size":1}},
          {"id":"a.dep","status":"deprecated","name":{"en":"d2"},"version":"1.0.0","category":"car","package":{"downloadUrl":"https://github.com/aliam664/Data/releases/download/t/a.zip","assetName":"a.zip","size":1}},
          {"id":"a.revoked","status":"revoked","revocationReason":"security issue","name":{"en":"r"},"version":"1.0.0","category":"car","package":{"downloadUrl":"https://github.com/aliam664/Data/releases/download/t/a.zip","assetName":"a.zip","size":1}}
        ]}
        """;
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(json));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        var ids = result.Catalog.Mods.Select(x => x.Id).ToArray();
        Assert.Contains("a.dep", ids);
        Assert.Contains("a.revoked", ids);
        Assert.DoesNotContain("a.draft", ids);
        Assert.DoesNotContain("a.hidden", ids);
        Assert.Equal(CatalogModStatus.Deprecated, result.Catalog.Mods.Single(x => x.Id == "a.dep").Status);
        var revoked = result.Catalog.Mods.Single(x => x.Id == "a.revoked");
        Assert.False(revoked.IsInstallable);
        Assert.NotNull(revoked.BlockReason);
    }

    [Fact]
    public async Task RevokedWithoutReason_IsRejected()
    {
        var json = """{"schemaVersion":1,"mods":[{"id":"a.revoked","status":"revoked","name":{"en":"r"},"version":"1.0.0","category":"car","package":{"downloadUrl":"https://github.com/aliam664/Data/releases/download/t/a.zip","assetName":"a.zip","size":1}}]}""";
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(json));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("embedded", result.Source);
    }

    [Fact]
    public async Task UnsafePackageName_IsRejected()
    {
        var json = """{"schemaVersion":1,"mods":[{"id":"a.b","status":"published","name":{"en":"x"},"version":"1.0.0","category":"car","package":{"downloadUrl":"https://github.com/aliam664/Data/releases/download/t/con.zip","assetName":"con.zip","size":1}}]}""";
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(json));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("embedded", result.Source);
    }

    [Fact]
    public async Task InvalidSha256_IsRejected()
    {
        var json = """{"schemaVersion":1,"mods":[{"id":"a.b","status":"published","name":{"en":"x"},"version":"1.0.0","category":"car","package":{"downloadUrl":"https://github.com/aliam664/Data/releases/download/t/a.zip","assetName":"a.zip","size":1,"sha256":"xyz"}}]}""";
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(json));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("embedded", result.Source);
    }

    [Fact]
    public async Task NonHttpsPackageUrl_IsRejected()
    {
        var json = """{"schemaVersion":1,"mods":[{"id":"a.b","status":"published","name":{"en":"x"},"version":"1.0.0","category":"car","package":{"downloadUrl":"http://github.com/aliam664/Data/releases/download/t/a.zip","assetName":"a.zip","size":1}}]}""";
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(json));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("embedded", result.Source);
    }

    [Fact]
    public async Task ModCountWithinLimit_IsAccepted()
    {
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(BuildModsJson(30)));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("primary", result.Source);
        Assert.Equal(30, result.Catalog.Mods.Count);
    }

    [Fact]
    public async Task ModCountLimit_IsEnforced()
    {
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(BuildModsJson(2001)));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("embedded", result.Source);
    }

    private static string BuildModsJson(int count)
    {
        var builder = new System.Text.StringBuilder("""{"schemaVersion":1,"mods":[""");
        for (var i = 0; i < count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append($$"""{"id":"a.m{{i}}","status":"published","name":{"en":"x"},"version":"1.0.0","category":"car","package":{"downloadUrl":"https://github.com/aliam664/Data/releases/download/t/a.zip","assetName":"a.zip","size":1}}""");
        }
        builder.Append("]}");
        return builder.ToString();
    }

    [Fact]
    public async Task OversizedPayload_IsRejected()
    {
        var big = "{\"schemaVersion\":1,\"mods\":[],\"padding\":\"" + new string('x', 2 * 1024 * 1024 + 1024) + "\"}";
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(big));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("embedded", result.Source);
    }

    [Fact]
    public async Task UnicodePersian_NamesSurviveRoundTrip()
    {
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(OneModJson));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("ماشین تست", result.Catalog.Mods[0].Name.Get("fa"));
        Assert.Equal("Test car", result.Catalog.Mods[0].Name.Get("en"));
    }

    [Fact]
    public async Task CatalogMinimumNewerThanLauncher_ProducesWarning()
    {
        var json = OneModJson.Replace("\"minimumLauncherVersion\": \"2.0.0\",", "\"minimumLauncherVersion\": \"99.0.0\",", StringComparison.Ordinal);
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(json));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("primary", result.Source);
        Assert.Equal(CatalogWarning.CatalogRequiresNewerLauncher, result.Warning);
    }

    [Fact]
    public async Task ModMinimumNewerThanLauncher_BlocksThatMod()
    {
        var json = OneModJson.Replace("\"tags\": [\"tag1\"],", "\"tags\": [\"tag1\"], \"minimumLauncherVersion\": \"99.0.0\",", StringComparison.Ordinal);
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(json));
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        var mod = Assert.Single(result.Catalog.Mods);
        Assert.False(mod.IsInstallable);
        Assert.StartsWith("requires-launcher:", mod.BlockReason);
    }

    [Fact]
    public async Task InsecureOverride_IsIgnored()
    {
        using var environment = Environment(new FakeCatalogHandler().EnqueueJson(OneModJson));
        var settings = environment.Get<ISettingsService>();
        var model = await settings.LoadAsync();
        model.CatalogUrl = "http://insecure.example/catalog.json";
        await settings.SaveAsync(model);
        var result = await environment.Get<IModCatalogService>().LoadAsync(true);
        Assert.Equal("primary", result.Source);
    }
}
