using System.Text.RegularExpressions;

namespace ACModHub.Core.Services;

public static partial class SafePath
{
    public static string NormalizeRelative(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.IndexOf('\0') >= 0)
            throw new UnsafeArchiveException("A path contains a null character.");

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.StartsWith("//", StringComparison.Ordinal) || DrivePathRegex().IsMatch(normalized) || Path.IsPathRooted(normalized))
            throw new UnsafeArchiveException($"Absolute path is not allowed: {path}");

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(x => x is "." or ".."))
            throw new UnsafeArchiveException($"Unsafe relative path: {path}");
        if (segments.Any(x => x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || x.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0))
            throw new UnsafeArchiveException($"Path contains invalid characters: {path}");
        if (segments.Any(x => x.EndsWith(' ') || x.EndsWith('.')))
            throw new UnsafeArchiveException($"Windows-trimmed path segments are not accepted: {path}");
        if (segments.Any(IsReservedWindowsName))
            throw new UnsafeArchiveException($"Reserved Windows device path is not accepted: {path}");

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    public static string CombineUnderRoot(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var normalized = NormalizeRelative(relativePath);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnsafeArchiveException($"Path escapes the allowed root: {relativePath}");
        EnsureNoReparsePoints(fullRoot, fullPath, relativePath);
        return fullPath;
    }

    private static void EnsureNoReparsePoints(string fullRoot, string fullPath, string originalRelativePath)
    {
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        var current = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnsafeArchiveException($"A symbolic link or reparse point was found in the destination path: {originalRelativePath}");
        }
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '1' and <= '9');
    }

    [GeneratedRegex("^[a-zA-Z]:($|[/\\\\])", RegexOptions.CultureInvariant)]
    private static partial Regex DrivePathRegex();

    /// <summary>
    /// Validates a single file name (no directory part) for use as a download destination:
    /// rejects separators, Windows reserved device names, trailing dots/spaces, ADS (":"),
    /// and characters that are invalid on Windows file systems.
    /// </summary>
    public static bool IsSafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Length > 240) return false;
        if (name.Contains('/') || name.Contains('\\') || name.Contains('\0')) return false;
        if (name.EndsWith(' ') || name.EndsWith('.')) return false;
        if (name.IndexOf(':') >= 0) return false; // drive qualifier or ADS stream
        if (name.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0) return false;
        if (IsReservedWindowsName(name)) return false;
        return true;
    }
}
