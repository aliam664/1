using System.IO.Compression;
using ACModHub.Core.Interfaces;
using ACModHub.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ACModHub.Tests.TestSupport;

public sealed class TestEnvironment : IDisposable
{
    public TestEnvironment(Action<IServiceCollection>? configure = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "acmodhub-tests", Guid.NewGuid().ToString("N"));
        GamePath = Path.Combine(Root, "game");
        Directory.CreateDirectory(Path.Combine(GamePath, "content"));
        Directory.CreateDirectory(Path.Combine(GamePath, "apps"));
        Directory.CreateDirectory(Path.Combine(GamePath, "system"));
        File.WriteAllText(Path.Combine(GamePath, "acs.exe"), "test executable marker");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddACModHubInfrastructure(Path.Combine(Root, "data"));
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
    }

    public string Root { get; }
    public string GamePath { get; }
    public ServiceProvider Services { get; }
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public string CreateZip(string name, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(Root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            var entry = archive.CreateEntry(entryPath, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return path;
    }

    public void Dispose()
    {
        Services.Dispose();
        try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch (IOException) { }
    }
}
