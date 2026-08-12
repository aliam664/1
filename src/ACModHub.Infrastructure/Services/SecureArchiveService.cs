using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ACModHub.Infrastructure.Services;

public sealed class SecureArchiveService : IArchiveService
{
    private const int MaximumEntryCount = 100_000;
    private const long MaximumUncompressedBytes = 20L * 1024 * 1024 * 1024;
    private const long MaximumSingleFileBytes = 8L * 1024 * 1024 * 1024;
    private const double MaximumCompressionRatio = 1_000d;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar" };
    private static readonly HashSet<string> RejectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".msi", ".msp", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".lnk", ".url"
    };

    public Task<ArchiveInspection> InspectAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        ValidateArchiveFile(archivePath);
        return Task.Run(() => InspectCore(archivePath, cancellationToken), cancellationToken);
    }

    public async Task ExtractAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        ValidateArchiveFile(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        // Inspect first and then validate every entry again while extracting. This also rejects duplicate paths.
        await InspectAsync(archivePath, cancellationToken).ConfigureAwait(false);
        using var archive = ArchiveFactory.Open(archivePath, new ReaderOptions { LeaveStreamOpen = false });
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory) continue;
            ValidateEntry(entry.Key, entry.Size, entry.CompressedSize, entry.IsEncrypted, entry.LinkTarget);
            var relative = SafePath.NormalizeRelative(entry.Key!);
            if (!extracted.Add(relative))
                throw new UnsafeArchiveException($"The archive contains a duplicate destination path: {entry.Key}");

            var destination = SafePath.CombineUnderRoot(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.OpenEntryStream();
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(target, 128 * 1024, cancellationToken).ConfigureAwait(false);
            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static ArchiveInspection InspectCore(string archivePath, CancellationToken cancellationToken)
    {
        using var archive = ArchiveFactory.Open(archivePath, new ReaderOptions { LeaveStreamOpen = false });
        var entries = new List<ArchiveEntryDescriptor>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.Count >= MaximumEntryCount)
                throw new UnsafeArchiveException($"The archive exceeds the {MaximumEntryCount:N0} entry safety limit.");

            ValidateEntry(entry.Key, entry.Size, entry.CompressedSize, entry.IsEncrypted, entry.LinkTarget);
            var normalized = SafePath.NormalizeRelative(entry.Key!).Replace('\\', '/');
            if (!entry.IsDirectory && !unique.Add(normalized))
                throw new UnsafeArchiveException($"The archive contains duplicate paths: {entry.Key}");

            if (!entry.IsDirectory)
            {
                checked { total += entry.Size; }
                if (total > MaximumUncompressedBytes)
                    throw new UnsafeArchiveException("The archive exceeds the 20 GiB extraction safety limit.");
            }
            entries.Add(new ArchiveEntryDescriptor(normalized, entry.Size, entry.CompressedSize, entry.IsDirectory, entry.IsEncrypted, !string.IsNullOrWhiteSpace(entry.LinkTarget)));
        }

        if (entries.All(x => x.IsDirectory))
            throw new UnsafeArchiveException("The archive is empty.");
        return new ArchiveInspection { ArchivePath = Path.GetFullPath(archivePath), Entries = entries };
    }

    private static void ValidateEntry(string? key, long size, long compressedSize, bool encrypted, string? linkTarget)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new UnsafeArchiveException("The archive contains an unnamed entry.");
        _ = SafePath.NormalizeRelative(key);
        if (encrypted)
            throw new UnsafeArchiveException($"Encrypted archive entries are not accepted: {key}");
        if (!string.IsNullOrWhiteSpace(linkTarget))
            throw new UnsafeArchiveException($"Symbolic or hard links are not accepted: {key}");
        if (size < 0 || size > MaximumSingleFileBytes)
            throw new UnsafeArchiveException($"An entry exceeds the 8 GiB per-file safety limit: {key}");
        if (compressedSize > 0 && size / (double)compressedSize > MaximumCompressionRatio)
            throw new UnsafeArchiveException($"A suspicious compression ratio was detected: {key}");
        if (RejectedExtensions.Contains(Path.GetExtension(key)))
            throw new UnsafeArchiveException($"Executable or command content is rejected by policy: {key}");
    }

    private static void ValidateArchiveFile(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath)) throw new FileNotFoundException("The mod archive was not found.", archivePath);
        if (!SupportedExtensions.Contains(Path.GetExtension(archivePath)))
            throw new UnsafeArchiveException("Only ZIP, 7Z and RAR archives are supported.");
    }
}
