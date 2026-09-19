using DevBot.Cli.Core;
using DevBot.Cli.Core.Modes;
using Spectre.Console;

namespace DevBot.Cli;

/// <summary>
/// Clase principal y punto de entrada (Entry Point) de la aplicación de consola DevBot.Cli.
/// Se encarga de:
/// 1. Parsear los argumentos de la línea de comandos (flags) o solicitarlos interactivamente.
/// 2. Configurar la codificación de la consola y renderizar la interfaz con Spectre.Console.
/// 3. Inicializar el contexto compartido (<see cref="AgentContext"/>).
/// 4. Manejar señales de cancelación del sistema operativo (Ctrl+C / SIGINT).
/// 5. Iniciar la ejecución del orquestador (<see cref="Orchestrator"/>).
/// </summary>
public class Program
{
    /// <summary>
    /// Método de inicio asíncrono de la aplicación.
    /// </summary>
    /// <param name="args">Array de argumentos pasados por la línea de comandos.</param>
    /// <returns>
    /// Código de salida del proceso (Exit Code):
    /// - 0: Tarea completada con éxito o comando informativo ejecutado.
    /// - 1: Error durante la ejecución o parámetros inválidos.
    /// - 130: Proceso abortado manualmente por el usuario mediante Ctrl+C.
    /// </returns>
    public static async Task<int> Main(string[] args)
    {
        // ---------------------------------------------------------------------
        // PASO 1: Configuración visual y de codificación
        // ---------------------------------------------------------------------
        // Asegura que caracteres especiales (emojis, tildes, box-drawing) se muestren correctamente
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Renderiza el logotipo ASCII estilizado "DevBot" con Spectre.Console
        AnsiConsole.Write(
            new FigletText("DevBot")
                .LeftJustified()
                .Color(Color.Aquamarine1));

        AnsiConsole.MarkupLine("[bold grey]Motor Agéntico Autónomo C# / .NET 8 con Semantic Kernel y Gemini[/]\n");

        // ---------------------------------------------------------------------
        // PASO 2: Manejo de comandos rápidos e informativos (--help, --report, --logs)
        // ---------------------------------------------------------------------
        // Si el usuario solicitó la ayuda, la mostramos y salimos sin ejecutar agentes
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return 0;
        }

        // Obtiene el directorio de trabajo del repositorio (--dir / -d), por defecto el directorio actual
        string repoDir = GetArg(args, "--dir", "-d") ?? Directory.GetCurrentDirectory();

        // Si se pasa --report, busca y muestra el último reporte ejecutivo generado en .agent/
        if (args.Contains("--report"))
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

        // Si se pasa --logs o --log, muestra las últimas 60 líneas del log de auditoría
        if (args.Contains("--logs") || args.Contains("--log"))
        {
            string logPath = Path.Combine(repoDir, ".agent", "logs", "latest.log");
            if (File.Exists(logPath))
            {
                string[] lines = File.ReadAllLines(logPath);
                int count = Math.Min(60, lines.Length);
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

        // ---------------------------------------------------------------------
        // PASO 3: Lectura y parsing de parámetros de ejecución
        // ---------------------------------------------------------------------
        // Descripción del requerimiento o feature a implementar
        string? taskDescription = GetArg(args, "--task", "-t");

        // Clave API de Gemini: prioridad 1) flag --api-key, 2) variable GEMINI_API_KEY, 3) caché local en ~/.devbot/api_key
        string? apiKey = GetArg(args, "--api-key", "-k") 
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? LoadPersistedApiKey();

        // Modelo de lenguaje a utilizar (por defecto gemini-3.5-flash-lite)
        string modelId = GetArg(args, "--model", "-m") ?? "gemini-3.5-flash-lite";

        // Cantidad máxima de reintentos en el bucle de corrección Coder-Reviewer (por defecto 3)
        string? retriesStr = GetArg(args, "--retries", "-r");
        int maxRetries = int.TryParse(retriesStr, out int r) && r > 0 ? r : 3;

        // Flag para omitir la confirmación humana interactiva (Human-in-the-Loop) en entornos CI/CD
        bool autoApprove = args.Contains("--yes") || args.Contains("-y");

        // ---------------------------------------------------------------------
        // PASO 4: Solicitud interactiva de datos faltantes (Fallback amigable)
        // ---------------------------------------------------------------------
        // Si no se pasó --task, solicita interactivamente al usuario qué desea construir
        if (string.IsNullOrWhiteSpace(taskDescription))
        {
            taskDescription = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold cyan]Describe la tarea de desarrollo o bugfix a resolver:[/]")
                    .PromptStyle("green")
                    .ValidationErrorMessage("[red]La descripción de la tarea no puede estar vacía[/]")
                    .Validate(text => !string.IsNullOrWhiteSpace(text)));
        }

        // Modo de operación: determinación (Forzado vs Auto-detección inteligente)
        string? modeArg = GetArg(args, "--mode", "-M");
        AgentMode mode;
        bool isForcedMode = !string.IsNullOrWhiteSpace(modeArg) && !modeArg.Equals("auto", StringComparison.OrdinalIgnoreCase);

        if (isForcedMode)
        {
            mode = ModePolicyRegistry.ParseMode(modeArg);
            AnsiConsole.MarkupLine($"[bold cyan]ℹ Modo de operación:[/] [bold magenta]{mode}[/] [grey](Forzado manualmente vía --mode)[/]");
        }
        else
        {
            var (detectedMode, reason) = ModePolicyRegistry.DetectMode(taskDescription);
            mode = detectedMode;
            AnsiConsole.MarkupLine($"[bold cyan]ℹ Modo de operación:[/] [bold yellow]{mode}[/] [grey](Auto-detectado: {Markup.Escape(reason)})[/]");
        }

        // Si no se detectó API Key en ninguna fuente, solicitarla de forma enmascarada (Secret) y persistirla
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            AnsiConsole.MarkupLine("[bold red]⚠ No se detectó GEMINI_API_KEY.[/]");
            apiKey = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold yellow]Por favor ingresa tu clave API de Gemini:[/]")
                    .Secret()
                    .ValidationErrorMessage("[red]La clave API no puede estar vacía[/]")
                    .Validate(key => !string.IsNullOrWhiteSpace(key)));

            SavePersistedApiKey(apiKey);
        }

        // Validación de existencia del directorio de trabajo
        if (!Directory.Exists(repoDir))
        {
            AnsiConsole.MarkupLine($"[bold red]Error:[/] El directorio '{repoDir}' no existe.");
            return 1;
        }

        // ---------------------------------------------------------------------
        // PASO 5: Creación del Contexto Compartido (AgentContext)
        // ---------------------------------------------------------------------
        // AgentContext contiene todo el estado mutable de la ejecución: rutas, métricas, logs, etc.
        var context = new AgentContext(repoDir, apiKey, modelId)
        {
            MaxRetries = maxRetries,
            AutoApprove = autoApprove
        };
        context.SetMode(mode);

        // ---------------------------------------------------------------------
        // PASO 6: Inicialización del Orquestador y Control de Cancelación (Ctrl+C)
        // ---------------------------------------------------------------------
        var orchestrator = new Orchestrator(context);

        try
        {
            // Token de cancelación cooperativo para abortar limpiamente si se presiona Ctrl+C
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true; // Evita la muerte abrupta del proceso para permitir limpieza
                AnsiConsole.MarkupLine("\n[bold red]Cancelación solicitada por el usuario. Abortando operaciones...[/]");
                cts.Cancel();
            };

            // Inicia el flujo autónomo: Git Branch -> Scout -> HITL -> Planner -> Coder/Reviewer -> PR
            bool success = await orchestrator.RunAsync(taskDescription, cts.Token);
            return success ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Operación cancelada por el usuario.[/]");
            return 130; // Código Unix estándar para procesos terminados con SIGINT
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error fatal no controlado:[/] {ex.Message}");
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    /// <summary>
    /// Utilidad para extraer el valor de un argumento a partir de su nombre largo o corto.
    /// Ejemplo: para '--task "Mi Tarea"', GetArg(args, "--task", "-t") devuelve "Mi Tarea".
    /// </summary>
    /// <param name="args">Array completo de argumentos de la línea de comandos.</param>
    /// <param name="longName">Nombre largo del flag (e.g. "--task").</param>
    /// <param name="shortName">Alias corto del flag (e.g. "-t").</param>
    /// <returns>El valor asociado al argumento, o null si no está presente.</returns>
    private static string? GetArg(string[] args, string longName, string shortName)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(longName, StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals(shortName, StringComparison.OrdinalIgnoreCase))
            {
                // Verifica que exista un siguiente elemento y que no sea otra bandera (que comience con '-')
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    return args[i + 1];
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Renderiza una tabla con el manual de ayuda de los comandos y flags disponibles en DevBot.
    /// </summary>
    private static void PrintHelp()
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
        table.AddRow("--api-key", "-k", "Clave API de Google Gemini (o variable GEMINI_API_KEY)", "[dim]Variable de entorno[/]");
        table.AddRow("--model", "-m", "Modelo de Gemini a utilizar", "gemini-3.5-flash-lite");
        table.AddRow("--retries", "-r", "Máximo de ciclos de corrección Coder-Reviewer", "3");
        table.AddRow("--report", "", "Visualiza el último informe ejecutivo generado en .agent/", "-");
        table.AddRow("--logs", "", "Visualiza las últimas líneas del registro de auditoría en .agent/logs/", "-");
        table.AddRow("--help", "-h", "Muestra esta ayuda", "-");

        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine("\n[bold yellow]Ejemplo de uso:[/] ");
        AnsiConsole.MarkupLine("  [dim]DevBot.Cli -t \"Agregar endpoint GET /health y tests unitarios\" -d ./MiProyecto[/]\n");
    }

    /// <summary>
    /// Intenta cargar la clave API de Gemini persistida previamente en el perfil del usuario (~/.devbot/api_key).
    /// </summary>
    /// <returns>La clave almacenada o null si el archivo no existe o está vacío.</returns>
    private static string? LoadPersistedApiKey()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".devbot", "api_key");
            if (File.Exists(path))
            {
                string key = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(key)) return key;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Guarda de forma persistente la clave API de Gemini en el perfil del usuario (~/.devbot/api_key)
    /// para evitar que el usuario tenga que ingresarla en cada ejecución.
    /// </summary>
    /// <param name="key">Clave API de Gemini a guardar.</param>
    private static void SavePersistedApiKey(string key)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".devbot");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "api_key"), key.Trim());
        }
        catch { }
    }
}
