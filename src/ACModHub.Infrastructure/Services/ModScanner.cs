using System.Security.Cryptography;
using System.Text;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;

namespace ACModHub.Infrastructure.Services;

public sealed class ModScanner : IModScanner
{
    private readonly IModRepository _repository;
    private readonly IFileHashService _hashes;

    public ModScanner(IModRepository repository, IFileHashService hashes)
    {
        _repository = repository;
        _hashes = hashes;
    }

    public async Task<IReadOnlyList<ModManifest>> ScanAsync(string gamePath, bool calculateHashes, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        var candidates = BuildCandidates(gamePath).ToArray();
        var manifests = new List<ModManifest>(candidates.Length);
        for (var index = 0; index < candidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            var allFiles = Directory.Exists(candidate.Path)
                ? Directory.EnumerateFiles(candidate.Path, "*", SearchOption.AllDirectories)
                : Enumerable.Empty<string>();
            if (candidate.ExcludeSkins) allFiles = allFiles.Where(x => !Path.GetRelativePath(candidate.Path, x).Replace('\\', '/').StartsWith("skins/", StringComparison.OrdinalIgnoreCase));
            var records = new List<ModFileRecord>();
            foreach (var fullPath in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(fullPath);
                var relative = Path.GetRelativePath(gamePath, fullPath).Replace('\\', '/');
                var hash = calculateHashes ? await _hashes.ComputeSha256Async(fullPath, cancellationToken).ConfigureAwait(false) : string.Empty;
                records.Add(new() { RelativePath = relative, Size = info.Length, Sha256 = hash });
            }
            if (records.Count == 0) continue;
            var manifest = new ModManifest
            {
                Id = StableId(candidate.Category + ":" + Path.GetRelativePath(gamePath, candidate.Path).Replace('\\', '/').ToLowerInvariant()),
                Name = FriendlyName(candidate.Name),
                Category = candidate.Category,
                Status = ModStatus.Unmanaged,
                Size = records.Sum(x => x.Size),
                Files = records
            };
            manifest.Metadata["scanRoot"] = Path.GetRelativePath(gamePath, candidate.Path).Replace('\\', '/');
            manifests.Add(manifest);
            progress?.Report((index + 1d) / Math.Max(1, candidates.Length));
        }
        return manifests;
    }

    public async Task ImportAsync(IEnumerable<ModManifest> manifests, CancellationToken cancellationToken = default)
    {
        foreach (var manifest in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            manifest.Status = ModStatus.Enabled;
            await _repository.SaveAsync(manifest, cancellationToken).ConfigureAwait(false);
            foreach (var file in manifest.Files)
            {
                var ownership = await _repository.GetOwnershipAsync(file.RelativePath, cancellationToken).ConfigureAwait(false)
                    ?? new FileOwnershipRecord { RelativePath = file.RelativePath };
                ownership.ModIds.Add(manifest.Id);
                ownership.UpdatedAt = DateTimeOffset.UtcNow;
                await _repository.SaveOwnershipAsync(ownership, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static IEnumerable<ScanCandidate> BuildCandidates(string gamePath)
    {
        foreach (var item in Children(gamePath, "content", "cars"))
        {
            yield return new(item, Path.GetFileName(item), ModCategory.Car, true);
            var skins = Path.Combine(item, "skins");
            if (Directory.Exists(skins))
                foreach (var skin in Directory.EnumerateDirectories(skins))
                    yield return new(skin, $"{Path.GetFileName(item)} · {Path.GetFileName(skin)}", ModCategory.Skin, false);
        }
        foreach (var item in Children(gamePath, "content", "tracks")) yield return new(item, Path.GetFileName(item), ModCategory.Track, false);
        foreach (var item in Children(gamePath, "content", "weather")) yield return new(item, Path.GetFileName(item), ModCategory.Weather, false);
        foreach (var item in Children(gamePath, "apps", "python")) yield return new(item, Path.GetFileName(item), ModCategory.App, false);
        var extension = Path.Combine(gamePath, "extension");
        if (Directory.Exists(extension)) yield return new(extension, "Custom Shaders Patch", ModCategory.Csp, false);
    }

    private static IEnumerable<string> Children(string root, params string[] parts)
    {
        var path = parts.Aggregate(root, Path.Combine);
        return Directory.Exists(path) ? Directory.EnumerateDirectories(path) : Enumerable.Empty<string>();
    }

    private static Guid StableId(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string FriendlyName(string input) => string.Join(' ', input.Replace('-', '_').Split('_', StringSplitOptions.RemoveEmptyEntries).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
    private sealed record ScanCandidate(string Path, string Name, ModCategory Category, bool ExcludeSkins);
}
