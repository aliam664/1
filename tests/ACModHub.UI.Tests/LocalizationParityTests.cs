using System.IO;
using System.Text.RegularExpressions;

namespace ACModHub.UI.Tests;

/// <summary>
/// Verifies that the Persian and English string tables define exactly the same keys
/// with non-empty values, so no visible text can silently fall back to the other language.
/// </summary>
public sealed partial class LocalizationParityTests
{
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ACModHub.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static Dictionary<string, string> ReadKeys(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"String resource file missing: {path}");
        var content = File.ReadAllText(path);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in KeyRegex().Matches(content))
        {
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value;
            Assert.False(string.IsNullOrWhiteSpace(value), $"Empty localized value for key '{key}' in {path}");
            result[key] = value;
        }
        return result;
    }

    [Fact]
    public void BothLanguages_DefineTheSameKeys()
    {
        var root = FindRepoRoot();
        var fa = ReadKeys(Path.Combine(root, "src", "ACModHub.UI", "Resources", "Strings.fa-IR.xaml"));
        var en = ReadKeys(Path.Combine(root, "src", "ACModHub.UI", "Resources", "Strings.en-US.xaml"));

        var missingInEn = fa.Keys.Except(en.Keys, StringComparer.Ordinal).ToArray();
        var missingInFa = en.Keys.Except(fa.Keys, StringComparer.Ordinal).ToArray();
        Assert.True(missingInEn.Length == 0, "Keys missing in en-US: " + string.Join(", ", missingInEn));
        Assert.True(missingInFa.Length == 0, "Keys missing in fa-IR: " + string.Join(", ", missingInFa));
        Assert.True(fa.Count > 100, $"Expected a comprehensive string table, found {fa.Count} keys");
    }

    [GeneratedRegex("x:Key=\"(?<key>[^\"]+)\">(?<value>[^<]*)", RegexOptions.CultureInvariant)]
    private static partial Regex KeyRegex();
}
