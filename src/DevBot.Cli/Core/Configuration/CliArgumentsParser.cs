using DevBot.Cli.Core.Modes;
using Spectre.Console;

namespace DevBot.Cli.Core.Configuration;

/// <summary>
/// Parser modular para los argumentos de la línea de comandos de DevBot.
/// </summary>
public static class CliArgumentsParser
{
    /// <summary>
    /// Parsea los argumentos pasados en la línea de comandos a una instancia inmutable de <see cref="CliOptions"/>.
    /// </summary>
    /// <param name="args">Array de argumentos del proceso.</param>
    /// <returns>Opciones estructuradas.</returns>
    public static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        bool showHelp = args.Contains("--help", StringComparer.OrdinalIgnoreCase) || 
                        args.Contains("-h", StringComparer.OrdinalIgnoreCase);

        bool showReport = args.Contains("--report", StringComparer.OrdinalIgnoreCase);

        bool showLogs = args.Contains("--logs", StringComparer.OrdinalIgnoreCase) || 
                        args.Contains("--log", StringComparer.OrdinalIgnoreCase);

        string repoDir = GetArg(args, "--dir", "-d") ?? Directory.GetCurrentDirectory();
        string? task = GetArg(args, "--task", "-t");
        string? apiKey = GetArg(args, "--api-key", "-k");
        string modelId = GetArg(args, "--model", "-m") ?? "gemini-3.5-flash-lite";

        string? retriesStr = GetArg(args, "--retries", "-r");
        int maxRetries = int.TryParse(retriesStr, out int r) && r > 0 ? r : 3;

        bool autoApprove = args.Contains("--yes", StringComparer.OrdinalIgnoreCase) || 
                           args.Contains("-y", StringComparer.OrdinalIgnoreCase);

        string? modeArg = GetArg(args, "--mode", "-M");
        AgentMode? explicitMode = null;
        if (!string.IsNullOrWhiteSpace(modeArg) && !modeArg.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            explicitMode = ModePolicyRegistry.ParseMode(modeArg);
        }

        return new CliOptions
        {
            ShowHelp = showHelp,
            ShowReport = showReport,
            ShowLogs = showLogs,
            RepoDir = repoDir,
            TaskDescription = task,
            ApiKey = apiKey,
            ModelId = modelId,
            MaxRetries = maxRetries,
            AutoApprove = autoApprove,
            ModeString = modeArg,
            ExplicitMode = explicitMode
        };
    }

    /// <summary>
    /// Renderiza la tabla de manual y parámetros disponibles en la consola.
    /// </summary>
    public static void PrintHelp()
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[bold cyan]Parámetro[/]");
        table.AddColumn("[bold cyan]Alias[/]");
        table.AddColumn("[bold cyan]Descripción[/]");
        table.AddColumn("[bold cyan]Valor por Defecto[/]");

        table.AddRow("--task", "-t", "Descripción del requerimiento, feature o bugfix a implementar", "[dim](Interactivo)[/]");
        table.AddRow("--mode", "-M", "Modo: auto | feature | bug | refactor | test (Auto-detecta según la tarea si se omite)", "auto");
        table.AddRow("--yes", "-y", "Aprobación automática de la fase Scout sin confirmación interactiva", "false");
        table.AddRow("--dir", "-d", "Directorio raíz del repositorio de destino", "[dim]Directorio actual[/]");
        table.AddRow("--api-key", "-k", "Clave API de Google Gemini (o variable GEMINI_API_KEY)", "[dim]Variable de entorno / ~/.devbot/api_key[/]");
        table.AddRow("--model", "-m", "Modelo de Gemini a utilizar", "gemini-3.5-flash-lite");
        table.AddRow("--retries", "-r", "Máximo de ciclos de corrección Coder-Reviewer", "3");
        table.AddRow("--report", "", "Visualiza el último informe ejecutivo generado en .agent/", "-");
        table.AddRow("--logs", "", "Visualiza las últimas líneas del registro de auditoría en .agent/logs/", "-");
        table.AddRow("--help", "-h", "Muestra esta ayuda", "-");

        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine("\n[bold yellow]Ejemplo de uso:[/] ");
        AnsiConsole.MarkupLine("  [dim]devbot -t \"Agregar endpoint GET /health y tests unitarios\" -d ./MiProyecto[/]\n");
    }

    private static string? GetArg(string[] args, string longName, string shortName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(longName, StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals(shortName, StringComparison.Ordinal))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    return args[i + 1];
                }
            }
        }
        return null;
    }
}
