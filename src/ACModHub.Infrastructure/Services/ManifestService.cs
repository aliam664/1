using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;

namespace ACModHub.Infrastructure.Services;

public sealed class ManifestService : IManifestService
{
    private readonly IFileHashService _hashes;

    public ManifestService(IFileHashService hashes) => _hashes = hashes;

    public async Task<VerificationResult> VerifyAsync(ModManifest manifest, string gamePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var issues = new List<VerificationIssue>();
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = manifest.Status == ModStatus.Disabled && !string.IsNullOrWhiteSpace(file.DisabledStorePath)
                ? file.DisabledStorePath!
                : SafePath.CombineUnderRoot(gamePath, file.RelativePath);
            if (!File.Exists(path))
            {
                issues.Add(new(file.RelativePath, "Missing file", file.Sha256, null));
                continue;
            }
            var actualSize = new FileInfo(path).Length;
            if (actualSize != file.Size)
            {
                issues.Add(new(file.RelativePath, $"Size mismatch (expected {file.Size:N0}, actual {actualSize:N0})", file.Sha256, null));
                continue;
            }
            if (string.IsNullOrWhiteSpace(file.Sha256)) continue;
            var actual = await _hashes.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            if (!actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(file.RelativePath, "SHA-256 mismatch", file.Sha256, actual));
        }
        return new VerificationResult { ModId = manifest.Id, Issues = issues };
    }
}
