using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Tools;
using Spectre.Console;

namespace DevBot.Cli.Core.Diagnostics;

/// <summary>
/// Servicio para la ejecución directa de pruebas del repositorio sin orquestación de LLM (modo --test-only).
/// </summary>
public static class TestRunnerService
{
    /// <summary>
    /// Detecta el stack tecnológico del repositorio y ejecuta su suite de pruebas con la estrategia correspondiente.
    /// </summary>
    /// <param name="repoRoot">Ruta raíz del repositorio.</param>
    /// <param name="ct">Token de cancelación cooperativa.</param>
    /// <returns>Código de salida del proceso (0 éxito, >0 fallo).</returns>
    public static async Task<int> RunTestsAsync(string repoRoot, CancellationToken ct = default)
    {
        var detector = new ProjectStackDetector();
        var stackInfo = await detector.DetectAsync(repoRoot, ct);
        var strategy = StrategyResolver.Resolve(stackInfo);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold cyan]🧪 DevBot: Ejecución de Pruebas (--test-only)[/]").RuleStyle("cyan"));
        AnsiConsole.MarkupLine($"[grey]Repositorio:[/] [dim]{repoRoot}[/]");
        AnsiConsole.MarkupLine($"[grey]Stack detectado:[/] [bold green]{stackInfo.StackType}[/] ([cyan]{stackInfo.Language}[/] vía [yellow]{stackInfo.BuildTool}[/])");
        AnsiConsole.MarkupLine($"[grey]Estrategia de prueba:[/] [bold]{strategy.Name}[/]\n");

        var terminal = new TerminalTools(repoRoot);
        var result = await strategy.TestAsync(terminal, ct);

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            AnsiConsole.WriteLine(result.Output);
        }

        if (result.Success)
        {
            AnsiConsole.MarkupLine("\n[bold green]✔ Todas las pruebas pasaron exitosamente.[/]\n");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"\n[bold red]❌ La suite de pruebas falló (Código de salida: {result.ExitCode}).[/]\n");
            return result.ExitCode != 0 ? result.ExitCode : 1;
        }
    }
}
