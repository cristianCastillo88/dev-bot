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
    /// <param name="globalConfig">Configuración global opcional para proveer valores por defecto.</param>
    /// <returns>Opciones estructuradas.</returns>
    public static CliOptions Parse(string[] args, GlobalConfig? globalConfig = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        globalConfig ??= GlobalConfigService.Load();

        string? subcommand = null;
        if (args.Length > 0 && KnownSubcommands.Contains(args[0]))
        {
            subcommand = args[0].ToLowerInvariant();
            if (subcommand == "log")
            {
                subcommand = "logs";
            }
        }

        bool showHelp = subcommand == "help" ||
                        args.Contains("--help", StringComparer.OrdinalIgnoreCase) || 
                        args.Contains("-h", StringComparer.OrdinalIgnoreCase);

        bool showReport = subcommand == "report" ||
                          args.Contains("--report", StringComparer.OrdinalIgnoreCase);

        bool showLogs = subcommand == "logs" ||
                        args.Contains("--logs", StringComparer.OrdinalIgnoreCase) || 
                        args.Contains("--log", StringComparer.OrdinalIgnoreCase);

        bool testOnly = subcommand == "test" ||
                        args.Contains("--test-only", StringComparer.OrdinalIgnoreCase);

        bool showStats = subcommand == "stats" ||
                         args.Contains("--stats", StringComparer.OrdinalIgnoreCase) ||
                         args.Contains("stats", StringComparer.OrdinalIgnoreCase);

        subcommand ??= showHelp ? "help" :
                       showReport ? "report" :
                       showLogs ? "logs" :
                       showStats ? "stats" :
                       testOnly ? "test" : "run";

        string repoDir = GetArg(args, "--dir", "-d") ?? Directory.GetCurrentDirectory();
        string? task = GetArg(args, "--task", "-t") ?? GetPositionalTask(args);
        string? apiKey = GetArg(args, "--api-key", "-k");
        string modelId = GetArg(args, "--model", "-m") ?? (!string.IsNullOrWhiteSpace(globalConfig.DefaultModel) ? globalConfig.DefaultModel : "gemini-3.5-flash-lite");

        string? retriesStr = GetArg(args, "--retries", "-r");
        int maxRetries = int.TryParse(retriesStr, out int r) && r > 0 
            ? r 
            : (globalConfig.MaxRetries > 0 ? globalConfig.MaxRetries : 3);

        bool autoApprove = args.Contains("--yes", StringComparer.OrdinalIgnoreCase) || 
                           args.Contains("-y", StringComparer.OrdinalIgnoreCase) ||
                           globalConfig.AutoApprove;

        bool continueExecution = args.Contains("--continue", StringComparer.OrdinalIgnoreCase) ||
                                 args.Contains("-c", StringComparer.OrdinalIgnoreCase);

        bool dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);

        string? provider = GetArg(args, "--provider", "-p") ?? globalConfig.DefaultProvider;

        bool interactive = args.Contains("--interactive", StringComparer.OrdinalIgnoreCase) ||
                            args.Contains("-i", StringComparer.OrdinalIgnoreCase);

        string? modeArg = GetArg(args, "--mode", "-M");
        AgentMode? explicitMode = null;
        if (!string.IsNullOrWhiteSpace(modeArg) && !modeArg.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            explicitMode = ModePolicyRegistry.ParseMode(modeArg);
        }

        return new CliOptions
        {
            Subcommand = subcommand,
            ShowHelp = showHelp,
            ShowReport = showReport,
            ShowLogs = showLogs,
            ShowStats = showStats,
            TestOnly = testOnly,
            RepoDir = repoDir,
            TaskDescription = task,
            ApiKey = apiKey,
            ModelId = modelId,
            MaxRetries = maxRetries,
            AutoApprove = autoApprove,
            ContinueExecution = continueExecution,
            DryRun = dryRun,
            Provider = provider,
            Interactive = interactive,
            ModeString = modeArg,
            ExplicitMode = explicitMode
        };
    }

    /// <summary>
    /// Renderiza la tabla de manual, subcomandos y parámetros disponibles en la consola.
    /// </summary>
    public static void PrintHelp()
    {
        AnsiConsole.MarkupLine("[bold cyan]Uso:[/] [yellow]devbot[/] [green][[subcomando]][/] [dim][[opciones]][/]\n");

        var cmdTable = new Table();
        cmdTable.Border(TableBorder.Rounded);
        cmdTable.AddColumn("[bold cyan]Subcomando[/]");
        cmdTable.AddColumn("[bold cyan]Descripción[/]");
        cmdTable.AddRow("run [dim][[tarea]][/]", "Ejecuta el ciclo de desarrollo autónomo (por defecto)");
        cmdTable.AddRow("test", "Ejecuta la suite de pruebas del proyecto sin llamadas LLM");
        cmdTable.AddRow("report", "Muestra el último informe ejecutivo generado en .agent/reports");
        cmdTable.AddRow("logs", "Muestra las últimas líneas del log de auditoría en .agent/logs");
        cmdTable.AddRow("stats", "Visualiza métricas históricas de ejecuciones previas");
        cmdTable.AddRow("help", "Muestra esta ayuda de comandos y opciones");
        AnsiConsole.Write(cmdTable);
        AnsiConsole.WriteLine();

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("[bold cyan]Parámetro[/]");
        table.AddColumn("[bold cyan]Alias[/]");
        table.AddColumn("[bold cyan]Descripción[/]");
        table.AddColumn("[bold cyan]Valor por Defecto[/]");

        table.AddRow("--task", "-t", "Descripción del requerimiento, feature o bugfix a implementar", "[dim](Interactivo)[/]");
        table.AddRow("--mode", "-M", "Modo: auto | feature | bug | refactor | test (Auto-detecta según la tarea si se omite)", "auto");
        table.AddRow("--yes", "-y", "Aprobación automática de la fase Scout sin confirmación interactiva", "false");
        table.AddRow("--continue", "-c", "Reanuda una ejecución previa desde el último hito no completado en .agent/", "false");
        table.AddRow("--dry-run", "", "Genera y valida el plan arquitectónico sin modificar archivos ni crear ramas", "false");
        table.AddRow("--test-only", "", "Ejecuta suite de pruebas del stack sin llamadas LLM", "false");
        table.AddRow("--stats", "", "Visualiza métricas históricas consolidadas de ejecuciones previas", "false");
        table.AddRow("--provider", "-p", "Proveedor de modelo LLM: gemini | openai | anthropic", "gemini");
        table.AddRow("--interactive", "-i", "Solicita aprobación humana individual antes de ejecutar cada hito", "false");
        table.AddRow("--dir", "-d", "Directorio raíz del repositorio de destino", "[dim]Directorio actual[/]");
        table.AddRow("--api-key", "-k", "Clave API del proveedor LLM (o variable GEMINI_API_KEY / OPENAI_API_KEY)", "[dim]Variable de entorno / ~/.devbot/api_key[/]");
        table.AddRow("--model", "-m", "Modelo LLM a utilizar", "gemini-3.5-flash-lite");
        table.AddRow("--retries", "-r", "Máximo de ciclos de corrección Coder-Reviewer", "3");
        table.AddRow("--report", "", "Visualiza el último informe ejecutivo generado en .agent/", "-");
        table.AddRow("--logs", "", "Visualiza las últimas líneas del registro de auditoría en .agent/logs/", "-");
        table.AddRow("--help", "-h", "Muestra esta ayuda", "-");

        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine("\n[bold yellow]Ejemplos de uso:[/] ");
        AnsiConsole.MarkupLine("  [dim]devbot run \"Agregar endpoint GET /health y tests unitarios\" -d ./MiProyecto[/]");
        AnsiConsole.MarkupLine("  [dim]devbot test -d ./MiProyecto[/]");
        AnsiConsole.MarkupLine("  [dim]devbot stats[/]\n");
    }

    private static readonly HashSet<string> KnownSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "run", "report", "logs", "log", "stats", "test", "help"
    };

    private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--help", "-h",
        "--report",
        "--logs", "--log",
        "--yes", "-y",
        "--continue", "-c",
        "--dry-run",
        "--test-only",
        "--stats",
        "--interactive", "-i",
        "--task", "-t",
        "--mode", "-M",
        "--dir", "-d",
        "--api-key", "-k",
        "--model", "-m",
        "--retries", "-r",
        "--provider", "-p"
    };

    private static string? GetArg(string[] args, string longName, string shortName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(longName, StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals(shortName, StringComparison.Ordinal))
            {
                if (i + 1 < args.Length)
                {
                    string next = args[i + 1];
                    if (!KnownFlags.Contains(next))
                    {
                        return next;
                    }
                }
            }
        }
        return null;
    }

    private static string? GetPositionalTask(string[] args)
    {
        var consumedIndices = new HashSet<int>();
        var valueFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--task", "-t", "--mode", "-M", "--dir", "-d",
            "--api-key", "-k", "--model", "-m", "--retries", "-r", "--provider", "-p"
        };

        for (int i = 0; i < args.Length; i++)
        {
            if (valueFlags.Contains(args[i]))
            {
                consumedIndices.Add(i);
                if (i + 1 < args.Length)
                {
                    consumedIndices.Add(i + 1);
                }
            }
            else if (KnownFlags.Contains(args[i]))
            {
                consumedIndices.Add(i);
            }
        }

        int startIndex = (args.Length > 0 && KnownSubcommands.Contains(args[0])) ? 1 : 0;
        for (int i = startIndex; i < args.Length; i++)
        {
            if (!consumedIndices.Contains(i) && !KnownFlags.Contains(args[i]) && !KnownSubcommands.Contains(args[i]))
            {
                return args[i];
            }
        }

        return null;
    }
}
