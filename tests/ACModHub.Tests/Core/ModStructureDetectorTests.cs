using ACModHub.Core.Models;
using ACModHub.Core.Services;

namespace ACModHub.Tests.Core;

public sealed class ModStructureDetectorTests
{
    private readonly ModStructureDetector _detector = new();

    [Fact]
    public void Detect_RemovesWrapperAndRecognizesCar()
    {
        ArchiveEntryDescriptor[] entries = [
            new("download/content/cars/ks_test/ui/ui_car.json", 10, 10, false),
            new("download/content/cars/ks_test/data.acd", 20, 20, false)];
        var plan = _detector.Detect("test.zip", entries);
        Assert.Equal(ModCategory.Car, plan.Category);
        Assert.All(plan.Files, x => Assert.StartsWith("content/cars/ks_test/", x.DestinationPath));
        Assert.Contains(plan.Warnings, x => x.Contains("wrapper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_ReconstructsMissingTrackRoot()
    {
        ArchiveEntryDescriptor[] entries = [
            new("my_track/ui/ui_track.json", 10, 10, false), new("my_track/models.ini", 5, 5, false)];
        var plan = _detector.Detect("track.zip", entries);
        Assert.Equal(ModCategory.Track, plan.Category);
        Assert.Contains(plan.Files, x => x.DestinationPath == "content/tracks/my_track/models.ini");
    }

    [Fact]
    public void Detect_ReportsMixedPackageWithoutDiscardingAnyFiles()
    {
        ArchiveEntryDescriptor[] entries = [
            new("content/cars/mixed/data.acd", 10, 10, false),
            new("extension/config/cars/mixed.ini", 5, 5, false)];
        var plan = _detector.Detect("mixed.zip", entries);
        Assert.Equal(ModCategory.Mixed, plan.Category);
        Assert.Equal(2, plan.Files.Count);
    }

    [Fact]
    public void Detect_InfersCarForStructuredSkin()
    {
        ArchiveEntryDescriptor[] entries = [new("wrapper/ks_car/skins/red/ui_skin.json", 10, 10, false), new("wrapper/ks_car/skins/red/livery.png", 5, 5, false)];
        var plan = _detector.Detect("skin.zip", entries);
        Assert.Equal(ModCategory.Skin, plan.Category);
        Assert.Contains(plan.Files, x => x.DestinationPath == "content/cars/ks_car/skins/red/livery.png");
    }

    [Fact]
    public void Detect_RecognizesPythonApp()
    {
        ArchiveEntryDescriptor[] entries = [new("telemetry/app.py", 10, 10, false), new("telemetry/icon.png", 5, 5, false)];
        var plan = _detector.Detect("app.zip", entries);
        Assert.Equal(ModCategory.App, plan.Category);
        Assert.Contains(plan.Files, x => x.DestinationPath == "apps/python/telemetry/app.py");
    }
}
