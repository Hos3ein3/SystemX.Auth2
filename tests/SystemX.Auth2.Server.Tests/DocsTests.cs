using System.Text.RegularExpressions;

namespace SystemX.Auth2.Server.Tests;

/// <summary>Documentation checks: every relative link in the Markdown files points to a file, and every anchor to a heading.</summary>
public partial class DocsTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SystemX.Auth2.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex LinkPattern();

    private static string Slug(string heading)
    {
        var s = heading.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[`*]", "");   // GitHub keeps underscores in anchors (#user_projects)
        s = Regex.Replace(s, @"[^\p{L}\p{N}\s_-]", "");
        return Regex.Replace(s.Replace(' ', '-'), "-+$", "");
    }

    private static HashSet<string> Anchors(string file) =>
        [.. File.ReadAllLines(file).Where(l => l.StartsWith('#')).Select(l => Slug(l.TrimStart('#')))];

    private static IEnumerable<string> MarkdownFiles(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories).Append(Path.Combine(root, "README.md"));

    [Fact]
    public void EveryRelativeLink_PointsToAnExistingFile_AndHeading()
    {
        var root = Root();
        var broken = new List<string>();
        foreach (var file in MarkdownFiles(root))
        {
            var inFence = false;
            foreach (var line in File.ReadLines(file))
            {
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
                if (inFence) continue;
                foreach (Match m in LinkPattern().Matches(line))
                {
                    var target = m.Groups[1].Value;
                    if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;
                    var parts = target.Split('#', 2);
                    var path = parts[0].Length == 0 ? file : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, parts[0]));
                    if (!File.Exists(path) && !Directory.Exists(path)) { broken.Add($"{Path.GetRelativePath(root, file)}: {target} (no file)"); continue; }
                    if (parts.Length == 2 && File.Exists(path) && path.EndsWith(".md", StringComparison.Ordinal) && !Anchors(path).Contains(parts[1]))
                        broken.Add($"{Path.GetRelativePath(root, file)}: {target} (no heading)");
                }
            }
        }
        broken.ShouldBeEmpty();
    }

    [Fact]
    public void ExternalIdentityGuides_HaveAUseAndAnExtendPart()
    {
        var root = Root();
        var missing = Directory.EnumerateFiles(Path.Combine(root, "docs", "external-identity"), "*.md")
            .Where(f => !Path.GetFileName(f).Equals("troubleshooting.md", StringComparison.Ordinal))
            .Where(f => { var t = File.ReadAllText(f); return !t.Contains("How to use", StringComparison.Ordinal) || !t.Contains("How to implement", StringComparison.Ordinal); })
            .Select(Path.GetFileName).ToList();
        missing.ShouldBeEmpty();
    }

    [Fact]
    public void NoSecretLookingValue_IsInTheDocs()
    {
        var root = Root();
        var suspicious = MarkdownFiles(root).Where(f => Regex.IsMatch(File.ReadAllText(f), @"(AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----\s*MI)")).Select(f => Path.GetRelativePath(root, f)).ToList();
        suspicious.ShouldBeEmpty();
    }
}
