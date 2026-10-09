using System.Text.RegularExpressions;
using Spectre.Console;

namespace DevBot.Cli.Core.Diagnostics;

/// <summary>
/// Modelo de datos para las estadísticas de una ejecución histórica de DevBot.
/// </summary>
public record RunStatsEntry(
    string ReportFile,
    string Timestamp,
    string TaskDescription,
    string Duration,
    string Tokens,
    int Retries,
    bool Success
);

/// <summary>
/// Servicio para analizar y presentar estadísticas históricas de ejecuciones de DevBot.
/// </summary>
public static class HistoricalStatsService
{
    /// <summary>
    /// Escanea los informes en .agent/reports/ y extrae las métricas históricas de ejecución.
    /// </summary>
    /// <param name="repoRoot">Directorio raíz del repositorio.</param>
    /// <returns>Lista de métricas de corridas previas.</returns>
    public static List<RunStatsEntry> LoadStats(string repoRoot)
    {
        var entries = new List<RunStatsEntry>();
        string reportsDir = Path.Combine(repoRoot, ".agent", "reports");

        var files = new List<string>();
        if (Directory.Exists(reportsDir))
        {
            files.AddRange(Directory.EnumerateFiles(reportsDir, "report_*.md", SearchOption.TopDirectoryOnly));
        }

        string latestReport = Path.Combine(repoRoot, ".agent", "latest_run_report.md");
        if (File.Exists(latestReport) && files.Count == 0)
        {
            files.Add(latestReport);
        }

        foreach (var file in files.OrderByDescending(f => File.GetCreationTimeUtc(f)))
        {
            try
            {
                string text = File.ReadAllText(file);
                var entry = ParseReport(Path.GetFileName(file), text);
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal parsing stats from {file}: {ex.Message}");
            }
        }

        return entries;
    }

    /// <summary>
    /// Renderiza en consola el dashboard consolidado de estadísticas históricas con Spectre.Console.
    /// </summary>
    /// <param name="repoRoot">Directorio raíz del repositorio.</param>
    /// <returns>Código de salida (0 éxito).</returns>
    public static int ShowStats(string repoRoot)
    {
        var entries = LoadStats(repoRoot);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold yellow]📊 DevBot: Historial de Telemetría y Rendimiento[/]").RuleStyle("yellow"));

        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No se encontraron informes de ejecución previos en .agent/reports/.[/]\n");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold cyan]Fecha / Informe[/]"));
        table.AddColumn(new TableColumn("[bold]Requerimiento[/]").Width(32));
        table.AddColumn(new TableColumn("[bold]Duración[/]"));
        table.AddColumn(new TableColumn("[bold]Tokens[/]"));
        table.AddColumn(new TableColumn("[bold]Ciclos[/]"));
        table.AddColumn(new TableColumn("[bold]Resultado[/]"));

        int totalTokens = 0;
        int successfulRuns = 0;

        foreach (var run in entries)
        {
            string statusStr = run.Success ? "[bold green]✔ ÉXITO[/]" : "[bold red]❌ REVERTIDO[/]";
            if (run.Success) successfulRuns++;

            if (int.TryParse(run.Tokens.Replace(",", "").Replace(".", "").Trim(), out int tCount))
            {
                totalTokens += tCount;
            }

            string taskSnippet = run.TaskDescription.Length > 40
                ? run.TaskDescription[..37] + "..."
                : run.TaskDescription;

            table.AddRow(
                Markup.Escape(run.Timestamp),
                Markup.Escape(taskSnippet),
                Markup.Escape(run.Duration),
                Markup.Escape(run.Tokens),
                run.Retries.ToString(),
                statusStr
            );
        }

        AnsiConsole.Write(table);

        double successRate = entries.Count > 0 ? (double)successfulRuns / entries.Count * 100.0 : 0.0;

        var summaryTable = new Table().Border(TableBorder.Minimal);
        summaryTable.AddColumn("[bold]Total Misiones[/]");
        summaryTable.AddColumn("[bold]Tasa de Éxito[/]");
        summaryTable.AddColumn("[bold]Tokens Totales[/]");

        summaryTable.AddRow(
            entries.Count.ToString(),
            $"{successRate:F1}% ({successfulRuns}/{entries.Count})",
            $"{totalTokens:N0}"
        );

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        return 0;
    }

    private static RunStatsEntry? ParseReport(string fileName, string markdown)
    {
        string timestamp = Regex.Match(markdown, @"\*\*Fecha y Hora:\*\*\s*([^\r\n]+)")?.Groups[1].Value.Trim() ?? fileName;
        bool isSuccess = markdown.Contains("COMPLETADO CON ÉXITO") || markdown.Contains("ALL_PASS");

        string task = string.Empty;
        var taskMatch = Regex.Match(markdown, @"## 2\. Requerimiento Asignado\s+```text\s+([^`]+)```", RegexOptions.Singleline);
        if (taskMatch.Success)
        {
            task = taskMatch.Groups[1].Value.Trim();
        }

        string duration = Regex.Match(markdown, @"\*\*Tiempo Total de Ejecución:\*\*\s*([^\r\n]+)")?.Groups[1].Value.Trim() ?? "-";
        string tokens = Regex.Match(markdown, @"\*\*Tokens Totales Consumidos:\*\*\s*([^\r\n]+)")?.Groups[1].Value.Trim() ?? "-";
        string retriesStr = Regex.Match(markdown, @"\*\*Ciclos Coder/Reviewer Requeridos:\*\*\s*(\d+)")?.Groups[1].Value.Trim() ?? "0";
        int.TryParse(retriesStr, out int retries);

        return new RunStatsEntry(
            fileName,
            timestamp,
            string.IsNullOrWhiteSpace(task) ? "(Sin descripción)" : task,
            duration,
            tokens,
            retries,
            isSuccess
        );
    }
}
