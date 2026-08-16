using System.IO;
using ACModHub.Core.Models;
using ACModHub.Infrastructure.Services;
using ACModHub.UI.Services;

namespace ACModHub.UI.Tests;

public sealed class StorePageBuilderTests
{
    private static StorePageBuilder CreateBuilder()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "acmodhub-tests", Guid.NewGuid().ToString("N")));
        return new StorePageBuilder(paths);
    }

    [Fact]
    public void EscapeForScript_NeutralizesClosingScriptTag()
    {
        var escaped = StorePageBuilder.EscapeForScript("</script><script>alert(1)</script>");
        Assert.DoesNotContain("</script>", escaped, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<\\/script", escaped);
    }

    [Fact]
    public void EscapeForScript_NeutralizesHtmlAndUnicode()
    {
        var escaped = StorePageBuilder.EscapeForScript("<b>& \u2028 \u2029");
        Assert.DoesNotContain("<", escaped);
        Assert.DoesNotContain(">", escaped);
        Assert.DoesNotContain("&", escaped);
        Assert.DoesNotContain("\u2028", escaped);
        Assert.DoesNotContain("\u2029", escaped);
        Assert.Contains("\\u003c", escaped);
    }

    [Fact]
    public void Build_WritesPageWithSafePayload()
    {
        var builder = CreateBuilder();
        var catalog = new ModCatalog
        {
            Mods =
            [
                new CatalogMod
                {
                    Id = "evil.script",
                    Status = CatalogModStatus.Published,
                    Name = new LocalizedText("</script><script>alert(1)</script>", "</script><script>alert(2)</script>"),
                    AuthorName = "<img src=x onerror=alert(3)>",
                    Version = "1.0.0",
                    Category = ModCategory.Car,
                    Description = new LocalizedText("d", "d")
                }
            ]
        };
        var strings = new Dictionary<string, string> { ["StoreTitle"] = "فروشگاه", ["StoreInstall"] = "نصب" };
        var path = builder.Build(catalog, "fa", strings, "منبع", string.Empty);
        Assert.True(File.Exists(path));
        var html = File.ReadAllText(path);
        Assert.DoesNotContain("</script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<\\/script", html);
        Assert.Contains("alert(1)", html); // data survives as inert text
        Assert.Contains("فروشگاه", html);
    }

    [Fact]
    public void Build_ExcludesDraftAndHiddenFromPayload()
    {
        var builder = CreateBuilder();
        var catalog = new ModCatalog
        {
            Mods =
            [
                new CatalogMod { Id = "a.pub", Status = CatalogModStatus.Published, Name = new LocalizedText("p", "p"), Version = "1.0.0" },
                new CatalogMod { Id = "a.draft", Status = CatalogModStatus.Draft, Name = new LocalizedText("d", "d"), Version = "1.0.0" },
                new CatalogMod { Id = "a.hidden", Status = CatalogModStatus.Hidden, Name = new LocalizedText("h", "h"), Version = "1.0.0" }
            ]
        };
        var path = builder.Build(catalog, "en", new Dictionary<string, string>(), "src", string.Empty);
        var html = File.ReadAllText(path);
        Assert.Contains("a.pub", html);
        Assert.DoesNotContain("a.draft", html);
        Assert.DoesNotContain("a.hidden", html);
    }
}
