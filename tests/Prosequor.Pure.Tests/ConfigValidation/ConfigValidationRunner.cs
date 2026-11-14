using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Prosequor.Pure.Tests.ConfigValidation;

/// <summary>
/// Walks shipped <c>config/prosequor</c> JSON and runs every registered rule that
/// claims the file. Does not call the skill compiler.
/// </summary>
public sealed class ConfigValidationRunner
{
    readonly IReadOnlyList<IConfigRule> rules;

    public ConfigValidationRunner(IEnumerable<IConfigRule> rules)
    {
        this.rules = rules.ToArray();
    }

    public IReadOnlyList<ConfigFinding> Scan(string repoRoot)
    {
        string configRoot = Path.Combine(repoRoot, "assets", "prosequor", "config", "prosequor");
        List<ConfigFinding> findings = new();
        if (!Directory.Exists(configRoot))
        {
            findings.Add(new ConfigFinding(
                RelativePath: "",
                Pointer: "/",
                RuleName: "runner",
                Message: $"Missing config directory: {configRoot}"));
            return findings;
        }

        string[] files = Directory.GetFiles(configRoot, "*.json", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string absolutePath in files)
        {
            string relativePath = Path.GetRelativePath(configRoot, absolutePath)
                .Replace('\\', '/');
            JToken root;
            try
            {
                using StreamReader reader = File.OpenText(absolutePath);
                using JsonTextReader jsonReader = new(reader);
                root = JToken.Load(jsonReader);
            }
            catch (Exception ex)
            {
                findings.Add(new ConfigFinding(
                    relativePath,
                    "/",
                    "runner",
                    $"JSON parse failed: {ex.Message}"));
                continue;
            }

            foreach (IConfigRule rule in rules)
            {
                if (!rule.AppliesTo(relativePath))
                {
                    continue;
                }

                rule.Check(relativePath, root, findings);
            }
        }

        return findings;
    }

    public static string FormatReport(IReadOnlyList<ConfigFinding> findings)
    {
        StringBuilder sb = new();
        sb.Append("[prosequor] Config validation failed (")
            .Append(findings.Count)
            .Append(" finding(s)):");
        foreach (IGrouping<string, ConfigFinding> group in findings
                     .GroupBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("\n\n").Append(string.IsNullOrEmpty(group.Key) ? "(root)" : group.Key)
                .Append(':');
            foreach (ConfigFinding finding in group)
            {
                sb.Append("\n  - [").Append(finding.RuleName).Append("] ")
                    .Append(finding.Pointer)
                    .Append(' ')
                    .Append(finding.Message);
            }
        }

        return sb.ToString();
    }

    public static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string probe = Path.Combine(
                dir.FullName,
                "assets",
                "prosequor",
                "config",
                "prosequor",
                "skills");
            if (Directory.Exists(probe))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "[prosequor] Could not locate repo root from "
            + AppContext.BaseDirectory
            + " (expected assets/prosequor/config/prosequor/skills).");
    }
}
