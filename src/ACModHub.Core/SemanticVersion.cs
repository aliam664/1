using System.Text.RegularExpressions;

namespace ACModHub.Core;

/// <summary>
/// Semantic Version 2.0.0 parsing and comparison (without build metadata).
/// Supports optional "v" prefix found in GitHub release tags.
/// </summary>
public readonly partial struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }
    public bool IsPrerelease => !string.IsNullOrEmpty(Prerelease);

    public SemanticVersion(int major, int minor, int patch, string? prerelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.Length > 128) return false;
        if (text[0] is 'v' or 'V') text = text[1..];
        var match = VersionRegex().Match(text);
        if (!match.Success) return false;
        if (int.TryParse(match.Groups[1].Value, out var major)
            && int.TryParse(match.Groups[2].Value, out var minor)
            && int.TryParse(match.Groups[3].Value, out var patch))
        {
            version = new SemanticVersion(major, minor, patch, match.Groups[4].Success ? match.Groups[4].Value : null);
            return true;
        }
        return false;
    }

    public static SemanticVersion Parse(string value) =>
        TryParse(value, out var version) ? version : throw new FormatException($"Invalid semantic version: {value}");

    public int CompareTo(SemanticVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string? left, string? right)
    {
        // Per SemVer: a version with a prerelease has LOWER precedence than the same version without.
        if (string.IsNullOrEmpty(left)) return string.IsNullOrEmpty(right) ? 0 : 1;
        if (string.IsNullOrEmpty(right)) return -1;
        var leftIdentifiers = left.Split('.');
        var rightIdentifiers = right.Split('.');
        var length = Math.Min(leftIdentifiers.Length, rightIdentifiers.Length);
        for (var i = 0; i < length; i++)
        {
            var leftIsNumeric = long.TryParse(leftIdentifiers[i], out var leftNumber);
            var rightIsNumeric = long.TryParse(rightIdentifiers[i], out var rightNumber);
            if (leftIsNumeric && rightIsNumeric)
            {
                var numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0) return numeric;
            }
            else
            {
                // Numeric identifiers always have lower precedence than alphanumeric identifiers.
                if (leftIsNumeric) return -1;
                if (rightIsNumeric) return 1;
                var text = string.Compare(leftIdentifiers[i], rightIdentifiers[i], StringComparison.OrdinalIgnoreCase);
                if (text != 0) return text;
            }
        }
        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    public bool Equals(SemanticVersion other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.IsNullOrEmpty(Prerelease) ? null : Prerelease.ToLowerInvariant());
    public override string ToString() => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";
    public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);
    public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}
