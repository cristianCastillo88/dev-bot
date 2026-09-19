using System.Text.RegularExpressions;
using DevBot.Cli.Core.Modes;
using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core.Interactive;

public static class ScoutPlanExtractor
{
    public static ScoutPlanSummary ExtractSummary(
        string taskDescription,
        AgentMode mode,
        ProjectStackInfo stack,
        IBuildAndTestStrategy strategy,
        string scoutReport)
    {
        var targetFiles = ExtractTargetFiles(scoutReport);
        string snippet = ExtractSnippet(scoutReport);

        return new ScoutPlanSummary(
            TaskDescription: taskDescription,
            Mode: mode,
            Stack: stack,
            TargetFiles: targetFiles,
            StrategySummary: $"{strategy.Name} ({stack.BuildTool})",
            ScoutReportSnippet: snippet
        );
    }

    public static IReadOnlyList<string> ExtractTargetFiles(string scoutReport)
    {
        var files = new List<string>();
        if (string.IsNullOrWhiteSpace(scoutReport)) return files;

        var lines = scoutReport.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        bool inTargetSection = false;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();

            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                if (line.Contains("Target File", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Archivos", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Ubicaciones", StringComparison.OrdinalIgnoreCase))
                {
                    inTargetSection = true;
                    continue;
                }
                else if (inTargetSection)
                {
                    // Exited the target files section
                    break;
                }
            }

            if (inTargetSection && (line.StartsWith("-") || line.StartsWith("*") || line.StartsWith("•")))
            {
                string clean = line.TrimStart('-', '*', '•', ' ').Trim();
                // Extract file path from backticks `path/to/file` if present
                var match = Regex.Match(clean, @"`([^`]+)`");
                if (match.Success)
                {
                    files.Add(match.Groups[1].Value.Trim());
                }
                else if (!string.IsNullOrWhiteSpace(clean))
                {
                    // Take first token or clean string before colon/parentheses
                    string candidate = clean.Split(new[] { ':', '(', '—', '-' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    if (candidate.Contains('.') || candidate.Contains('/') || candidate.Contains('\\'))
                    {
                        files.Add(candidate);
                    }
                }
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ExtractSnippet(string scoutReport, int maxChars = 500)
    {
        if (string.IsNullOrWhiteSpace(scoutReport)) return "Sin reporte disponible.";

        string clean = scoutReport.Trim();
        if (clean.Length <= maxChars) return clean;

        return clean.Substring(0, maxChars) + "\n... [continúa en scout_report.md] ...";
    }
}
