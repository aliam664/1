using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;

namespace ACModHub.Core.Services;

public sealed class ModStructureDetector : IModStructureDetector
{
    private static readonly HashSet<string> GameRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "content", "apps", "extension", "system"
    };

    public ModInstallPlan Detect(string archiveName, IReadOnlyList<ArchiveEntryDescriptor> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);
        ArgumentNullException.ThrowIfNull(entries);

        var files = entries.Where(x => !x.IsDirectory && !IsPackageManifest(x.ArchivePath)).ToArray();
        if (files.Length == 0)
            throw new ModHubException("The archive does not contain any files.");

        var paths = files.Select(x => NormalizeArchivePath(x.ArchivePath)).ToArray();
        var warnings = new List<string>();

        var rooted = DetectExistingGameRoot(paths);
        if (rooted is not null)
            return CreateRootedPlan(archiveName, files, paths, rooted.Value.PrefixLength, warnings);

        var carRoot = FindMetadataRoot(paths, "ui/ui_car.json") ?? FindFileParent(paths, "data.acd");
        if (carRoot is not null)
            return CreateContentPlan(archiveName, files, paths, carRoot, "content/cars", ModCategory.Car, warnings);

        var trackRoot = FindMetadataRoot(paths, "ui/ui_track.json") ?? FindTrackRoot(paths);
        if (trackRoot is not null)
            return CreateContentPlan(archiveName, files, paths, trackRoot, "content/tracks", ModCategory.Track, warnings);

        var appRoot = FindAppRoot(paths);
        if (appRoot is not null)
            return CreateContentPlan(archiveName, files, paths, appRoot, "apps/python", ModCategory.App, warnings);

        var skinRoot = FindMetadataRoot(paths, "ui_skin.json");
        if (skinRoot is not null)
        {
            var rootParts = Split(skinRoot);
            var skinsIndex = Array.FindIndex(rootParts, x => x.Equals("skins", StringComparison.OrdinalIgnoreCase));
            if (skinsIndex >= 1 && skinsIndex + 1 < rootParts.Length)
                return CreateExactPlan(archiveName, files, paths, skinRoot, $"content/cars/{rootParts[skinsIndex - 1]}/skins/{rootParts[skinsIndex + 1]}", ModCategory.Skin, warnings);
            warnings.Add("A standalone skin was found. Select its destination car in Preview before installing.");
            return CreateContentPlan(archiveName, files, paths, skinRoot, "content/cars/_select_car_/skins", ModCategory.Skin, warnings);
        }

        var commonPrefix = GetCommonDirectoryPrefix(paths);
        warnings.Add("No canonical Assetto Corsa root was found; files are treated as a miscellaneous package.");
        var mappings = BuildMappings(files, paths, commonPrefix.Length, string.Empty);
        return new ModInstallPlan
        {
            SuggestedName = FriendlyName(Path.GetFileNameWithoutExtension(archiveName)),
            Category = ModCategory.Miscellaneous,
            RootPrefixRemoved = string.Join('/', commonPrefix),
            Files = mappings,
            Warnings = warnings
        };
    }

    private static (int PrefixLength, int RootIndex)? DetectExistingGameRoot(IReadOnlyList<string> paths)
    {
        var candidates = new List<(int PrefixLength, int RootIndex)>();
        foreach (var path in paths)
        {
            var parts = Split(path);
            for (var i = 0; i < parts.Length; i++)
            {
                if (GameRoots.Contains(parts[i]))
                {
                    candidates.Add((i, i));
                    break;
                }
            }
        }

        if (candidates.Count != paths.Count)
            return null;
        var prefixLength = candidates[0].PrefixLength;
        if (candidates.Any(x => x.PrefixLength != prefixLength))
            return null;

        var firstParts = Split(paths[0]);
        var prefix = firstParts.Take(prefixLength).ToArray();
        if (paths.Any(path => !Split(path).Take(prefixLength).SequenceEqual(prefix, StringComparer.OrdinalIgnoreCase)))
            return null;
        return (prefixLength, prefixLength);
    }

    private static ModInstallPlan CreateRootedPlan(
        string archiveName,
        IReadOnlyList<ArchiveEntryDescriptor> files,
        IReadOnlyList<string> paths,
        int prefixLength,
        List<string> warnings)
    {
        var mappings = BuildMappings(files, paths, prefixLength, string.Empty);
        var destinations = mappings.Select(x => x.DestinationPath.Replace('\\', '/')).ToArray();
        var categories = destinations.Select(CategoryFromDestination).Distinct().ToArray();
        var category = categories.Length == 1 ? categories[0] : ModCategory.Mixed;
        var identity = GetIdentity(destinations, category);
        if (prefixLength > 0)
            warnings.Add($"Removed {prefixLength} extra wrapper folder(s) from the archive.");
        if (categories.Length > 1)
            warnings.Add("The package contains more than one mod category.");

        return new ModInstallPlan
        {
            SuggestedName = FriendlyName(identity ?? Path.GetFileNameWithoutExtension(archiveName)),
            Category = category,
            RootPrefixRemoved = string.Join('/', Split(paths[0]).Take(prefixLength)),
            Files = mappings,
            Warnings = warnings
        };
    }

    private static ModInstallPlan CreateExactPlan(
        string archiveName,
        IReadOnlyList<ArchiveEntryDescriptor> files,
        IReadOnlyList<string> paths,
        string sourceRoot,
        string destinationRoot,
        ModCategory category,
        List<string> warnings)
    {
        var rootParts = Split(sourceRoot);
        if (paths.Any(path => !StartsWithSegments(Split(path), rootParts)))
            throw new ModHubException("The archive mixes unrelated files with a detected skin root.");
        warnings.Add("A missing Assetto Corsa content root was reconstructed automatically.");
        return new ModInstallPlan
        {
            SuggestedName = FriendlyName(rootParts.LastOrDefault() ?? Path.GetFileNameWithoutExtension(archiveName)),
            Category = category,
            RootPrefixRemoved = sourceRoot,
            Files = BuildMappings(files, paths, rootParts.Length, destinationRoot, includeRootName: false),
            Warnings = warnings
        };
    }

    private static ModInstallPlan CreateContentPlan(
        string archiveName,
        IReadOnlyList<ArchiveEntryDescriptor> files,
        IReadOnlyList<string> paths,
        string root,
        string destinationRoot,
        ModCategory category,
        List<string> warnings)
    {
        var rootParts = Split(root);
        var allInsideRoot = paths.All(path => StartsWithSegments(Split(path), rootParts));
        if (!allInsideRoot)
            throw new ModHubException("The archive mixes unrelated files with a detected mod root.");

        var name = rootParts.LastOrDefault() ?? Path.GetFileNameWithoutExtension(archiveName);
        var destination = $"{destinationRoot}/{name}";
        warnings.Add("A missing Assetto Corsa content root was reconstructed automatically.");
        return new ModInstallPlan
        {
            SuggestedName = FriendlyName(name),
            Category = category,
            RootPrefixRemoved = string.Join('/', rootParts.Take(Math.Max(0, rootParts.Length - 1))),
            Files = BuildMappings(files, paths, rootParts.Length, destination, includeRootName: false),
            Warnings = warnings
        };
    }

    private static IReadOnlyList<PlannedFile> BuildMappings(
        IReadOnlyList<ArchiveEntryDescriptor> files,
        IReadOnlyList<string> paths,
        int removeSegments,
        string destinationPrefix,
        bool includeRootName = true)
    {
        var result = new List<PlannedFile>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var parts = Split(paths[i]);
            var remainder = parts.Skip(removeSegments).ToArray();
            if (!includeRootName && remainder.Length == 0)
                continue;
            var destination = Join(destinationPrefix, string.Join('/', remainder));
            destination = SafePath.NormalizeRelative(destination).Replace('\\', '/');
            result.Add(new PlannedFile(paths[i], destination, files[i].UncompressedSize));
        }
        return result;
    }

    private static string? FindMetadataRoot(IEnumerable<string> paths, string marker)
    {
        foreach (var path in paths)
        {
            var normalized = path.Replace('\\', '/');
            var suffix = "/" + marker;
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return normalized[..^suffix.Length];
        }
        return null;
    }

    private static string? FindFileParent(IEnumerable<string> paths, string fileName)
    {
        var path = paths.FirstOrDefault(x => x.EndsWith('/' + fileName, StringComparison.OrdinalIgnoreCase));
        return path is null ? null : path[..^(fileName.Length + 1)];
    }

    private static string? FindTrackRoot(IEnumerable<string> paths)
    {
        var model = paths.FirstOrDefault(x => Path.GetFileName(x).StartsWith("models", StringComparison.OrdinalIgnoreCase)
                                           && x.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        return model is null ? null : model[..model.LastIndexOf('/')];
    }

    private static string? FindAppRoot(IEnumerable<string> paths)
    {
        var app = paths.FirstOrDefault(x => x.EndsWith("/app.py", StringComparison.OrdinalIgnoreCase));
        return app is null ? null : app[..^"/app.py".Length];
    }

    private static ModCategory CategoryFromDestination(string path)
    {
        var parts = Split(path);
        if (parts.Length >= 5 && parts[0].Equals("content", StringComparison.OrdinalIgnoreCase)
                              && parts[1].Equals("cars", StringComparison.OrdinalIgnoreCase)
                              && parts[3].Equals("skins", StringComparison.OrdinalIgnoreCase)) return ModCategory.Skin;
        if (parts.Length >= 2 && parts[0].Equals("content", StringComparison.OrdinalIgnoreCase)
                              && parts[1].Equals("cars", StringComparison.OrdinalIgnoreCase)) return ModCategory.Car;
        if (parts.Length >= 2 && parts[0].Equals("content", StringComparison.OrdinalIgnoreCase)
                              && parts[1].Equals("tracks", StringComparison.OrdinalIgnoreCase)) return ModCategory.Track;
        if (parts.Length >= 2 && parts[0].Equals("content", StringComparison.OrdinalIgnoreCase)
                              && parts[1].Equals("weather", StringComparison.OrdinalIgnoreCase)) return ModCategory.Weather;
        if (parts.Length >= 1 && parts[0].Equals("apps", StringComparison.OrdinalIgnoreCase)) return ModCategory.App;
        if (parts.Length >= 1 && parts[0].Equals("extension", StringComparison.OrdinalIgnoreCase)) return ModCategory.Csp;
        return ModCategory.Miscellaneous;
    }

    private static string? GetIdentity(IReadOnlyList<string> destinations, ModCategory category)
    {
        var index = category switch
        {
            ModCategory.Car or ModCategory.Track or ModCategory.Weather => 2,
            ModCategory.App => 2,
            _ => -1
        };
        if (index < 0) return null;
        var parts = Split(destinations[0]);
        return parts.Length > index ? parts[index] : null;
    }

    private static string[] GetCommonDirectoryPrefix(IReadOnlyList<string> paths)
    {
        var split = paths.Select(Split).ToArray();
        var max = split.Min(x => Math.Max(0, x.Length - 1));
        var length = 0;
        while (length < max && split.All(x => x[length].Equals(split[0][length], StringComparison.OrdinalIgnoreCase)))
            length++;
        return split[0].Take(length).ToArray();
    }

    private static bool StartsWithSegments(string[] value, string[] prefix) =>
        value.Length >= prefix.Length && value.Take(prefix.Length).SequenceEqual(prefix, StringComparer.OrdinalIgnoreCase);

    private static bool IsPackageManifest(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        return !normalized.Contains('/') && (normalized.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || normalized.Equals("acmodhub.manifest.json", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeArchivePath(string path) => SafePath.NormalizeRelative(path).Replace('\\', '/');
    private static string[] Split(string path) => path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    private static string Join(string left, string right) => string.IsNullOrWhiteSpace(left) ? right : string.IsNullOrWhiteSpace(right) ? left : $"{left.TrimEnd('/')}/{right.TrimStart('/')}";
    private static string FriendlyName(string value) => string.Join(' ', value.Replace('-', '_').Split('_', StringSplitOptions.RemoveEmptyEntries).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
}
