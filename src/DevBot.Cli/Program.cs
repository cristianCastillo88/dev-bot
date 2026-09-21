using DevBot.Cli.Core;
using DevBot.Cli.Core.Configuration;
using DevBot.Cli.Core.DependencyInjection;
using DevBot.Cli.Core.Diagnostics;
using DevBot.Cli.Core.Modes;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DevBot.Cli;

/// <summary>
/// Composition Root y punto de entrada de la aplicación de consola DevBot.Cli.
/// Inicializa la consola, resuelve dependencias mediante IoC y orquesta la ejecución.
/// </summary>
public class Program
{
    /// <summary>
    /// Método de inicio asíncrono de la aplicación.
    /// </summary>
    /// <param name="args">Array de argumentos pasados por la línea de comandos.</param>
    /// <returns>Código de salida del proceso (0 éxito, 1 error, 130 abortado por usuario).</returns>
    public static async Task<int> Main(string[] args)
    {
        ConfigureConsole();

        var options = CliArgumentsParser.Parse(args);
        if (TryHandleImmediateCommands(options, out int exitCode))
        {
            return exitCode;
        }

        if (!Directory.Exists(options.RepoDir))
        {
            AnsiConsole.MarkupLine($"[bold red]Error:[/] El directorio '{options.RepoDir}' no existe.");
            return 1;
        }

        using var serviceProvider = BuildServiceProvider();
        var credentialStorage = serviceProvider.GetRequiredService<ICredentialStorageService>();

        string taskDescription = ResolveTaskDescription(options);
        var mode = ResolveAgentMode(options, taskDescription);
        string apiKey = ResolveApiKey(options, credentialStorage);

        var context = CreateAgentContext(options, apiKey, mode);
        var orchestratorFactory = serviceProvider.GetRequiredService<IOrchestratorFactory>();
        var orchestrator = orchestratorFactory.Create(context);

        return await ExecuteWithCancellationAsync(orchestrator, taskDescription);
    }

    private static void ConfigureConsole()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        AnsiConsole.Write(new FigletText("DevBot").LeftJustified().Color(Color.Aquamarine1));
        AnsiConsole.MarkupLine("[bold grey]Motor Agéntico Autónomo C# / .NET 8 con Semantic Kernel y Gemini[/]\n");
    }

    private static bool TryHandleImmediateCommands(CliOptions options, out int exitCode)
    {
        if (options.ShowHelp)
        {
            CliArgumentsParser.PrintHelp();
            exitCode = 0;
            return true;
        }

        if (options.ShowReport)
        {
            exitCode = DiagnosticViewService.ShowLatestReport(options.RepoDir);
            return true;
        }

        if (options.ShowLogs)
        {
            exitCode = DiagnosticViewService.ShowRecentLogs(options.RepoDir);
            return true;
        }

        exitCode = 0;
        return false;
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDevBotCore();
        return services.BuildServiceProvider();
    }

    private static string ResolveTaskDescription(CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.TaskDescription))
        {
            return options.TaskDescription;
        }

        return AnsiConsole.Prompt(
            new TextPrompt<string>("[bold cyan]Describe la tarea de desarrollo o bugfix a resolver:[/]")
                .PromptStyle("green")
                .ValidationErrorMessage("[red]La descripción de la tarea no puede estar vacía[/]")
                .Validate(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static AgentMode ResolveAgentMode(CliOptions options, string taskDescription)
    {
        if (options.ExplicitMode.HasValue)
        {
            var mode = options.ExplicitMode.Value;
            AnsiConsole.MarkupLine($"[bold cyan]ℹ Modo de operación:[/] [bold magenta]{mode}[/] [grey](Forzado manualmente vía --mode)[/]");
            return mode;
        }

        var (detectedMode, reason) = ModePolicyRegistry.DetectMode(taskDescription);
        AnsiConsole.MarkupLine($"[bold cyan]ℹ Modo de operación:[/] [bold yellow]{detectedMode}[/] [grey](Auto-detectado: {Markup.Escape(reason)})[/]");
        return detectedMode;
    }

    private static string ResolveApiKey(CliOptions options, ICredentialStorageService credentialStorage)
    {
        string? apiKey = options.ApiKey 
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY") 
            ?? credentialStorage.LoadApiKey();

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey;
        }

        AnsiConsole.MarkupLine("[bold red]⚠ No se detectó GEMINI_API_KEY.[/]");
        apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>("[bold yellow]Por favor ingresa tu clave API de Gemini:[/]")
                .Secret()
                .ValidationErrorMessage("[red]La clave API no puede estar vacía[/]")
                .Validate(key => !string.IsNullOrWhiteSpace(key)));

        credentialStorage.SaveApiKey(apiKey);
        return apiKey;
    }

    private static AgentContext CreateAgentContext(CliOptions options, string apiKey, AgentMode mode)
    {
        var context = new AgentContext(options.RepoDir, apiKey, options.ModelId)
        {
            MaxRetries = options.MaxRetries,
            AutoApprove = options.AutoApprove
        };
        context.SetMode(mode);
        return context;
    }

    private static async Task<int> ExecuteWithCancellationAsync(IOrchestrator orchestrator, string taskDescription)
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            AnsiConsole.MarkupLine("\n[bold red]Cancelación solicitada por el usuario. Abortando operaciones...[/]");
            cts.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;
        try
        {
            bool success = await orchestrator.RunAsync(taskDescription, cts.Token);
            return success ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Operación cancelada por el usuario.[/]");
            return 130;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error fatal no controlado:[/] {ex.Message}");
            AnsiConsole.WriteException(ex);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
