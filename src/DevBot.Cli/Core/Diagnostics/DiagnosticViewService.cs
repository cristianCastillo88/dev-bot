using Spectre.Console;

namespace DevBot.Cli.Core.Diagnostics;

/// <summary>
/// Servicio que atiende los comandos de visualización rápida de diagnósticos (--report y --logs).
/// </summary>
public static class DiagnosticViewService
{
    /// <summary>
    /// Intenta leer y renderizar el último informe ejecutivo generado en .agent/latest_run_report.md.
    /// </summary>
    /// <param name="repoDir">Directorio del repositorio.</param>
    /// <returns>0 si se mostró con éxito, 1 si no se encontró.</returns>
    public static int ShowLatestReport(string repoDir)
    {
        string reportPath = Path.Combine(repoDir, ".agent", "latest_run_report.md");
        if (File.Exists(reportPath))
        {
            string content = File.ReadAllText(reportPath);
            AnsiConsole.Write(new Panel(new Text(content))
            {
                Header = new PanelHeader("[bold cyan] Último Informe Ejecutivo (.agent/latest_run_report.md) [/]"),
                Border = BoxBorder.Rounded
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[yellow]No se encontró un informe en '{reportPath}'. Ejecuta primero una tarea con DevBot.[/]");
        return 1;
    }

    /// <summary>
    /// Intenta leer y renderizar las últimas líneas del registro de auditoría en .agent/logs/latest.log.
    /// </summary>
    /// <param name="repoDir">Directorio del repositorio.</param>
    /// <param name="maxLines">Cantidad máxima de líneas recientes a mostrar (por defecto 60).</param>
    /// <returns>0 si se mostró con éxito, 1 si no se encontró.</returns>
    public static int ShowRecentLogs(string repoDir, int maxLines = 60)
    {
        string logPath = Path.Combine(repoDir, ".agent", "logs", "latest.log");
        if (File.Exists(logPath))
        {
            string[] lines = File.ReadAllLines(logPath);
            int count = Math.Min(maxLines, lines.Length);
            var recent = lines.Skip(lines.Length - count);
            AnsiConsole.Write(new Panel(new Text(string.Join(Environment.NewLine, recent)))
            {
                Header = new PanelHeader($"[bold grey] Últimas {count} líneas del log ({logPath}) [/]"),
                Border = BoxBorder.Rounded
            });
            return 0;
        }

        AnsiConsole.MarkupLine($"[yellow]No se encontró archivo de log en '{logPath}'.[/]");
        return 1;
    }
}
