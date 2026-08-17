using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ScrapMechanicModManager.Tests;

public sealed partial class PublicLanguageContractTests
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".axaml", ".cs", ".csproj", ".desktop", ".js", ".json", ".lua", ".md",
        ".mjs", ".ps1", ".sh", ".sln", ".txt", ".yaml", ".yml",
    };

    private static readonly HashSet<string> IntentionalTranslationFiles = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "src/ScrapMechanicModManager.Core/Localization/AppLocalizer.cs",
        "tests/ScrapMechanicModManager.Tests/AppLocalizerTests.cs",
    };

    [Fact]
    public void Tracked_public_technical_text_is_English()
    {
        string repoRoot = FindRepoRoot();
        IReadOnlyList<string> trackedFiles = GetTrackedFiles(repoRoot);
        var violations = new List<string>();

        foreach (string relativePath in trackedFiles)
        {
            string normalized = relativePath.Replace('\\', '/');
            if (IntentionalTranslationFiles.Contains(normalized)
                || !TextExtensions.Contains(Path.GetExtension(normalized)))
            {
                continue;
            }

            string fullPath = Path.Combine(repoRoot, relativePath);
            string[] lines = File.ReadAllLines(fullPath);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                if (HungarianAccentRegex().IsMatch(lines[lineIndex]))
                {
                    violations.Add($"{normalized}:{lineIndex + 1}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Hungarian technical text remains outside the translation catalog:\n" +
            string.Join(Environment.NewLine, violations));
    }

    private static IReadOnlyList<string> GetTrackedFiles(string repoRoot)
    {
        using var process = Process.Start(new ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        }) ?? throw new InvalidOperationException("Could not start git ls-files.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "robots_01.zip")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    [GeneratedRegex("[\\u00E1\\u00E9\\u00ED\\u00F3\\u00F6\\u0151\\u00FA\\u00FC\\u0171\\u00C1\\u00C9\\u00CD\\u00D3\\u00D6\\u0150\\u00DA\\u00DC\\u0170]")]
    private static partial Regex HungarianAccentRegex();
}
