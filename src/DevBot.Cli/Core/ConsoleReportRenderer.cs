using DevBot.Cli.Core.Planning;
using Spectre.Console;

namespace DevBot.Cli.Core;

/// <summary>
/// Responsable exclusivo de la presentación visual y renderizado de tablas, paneles
/// y resúmenes de ejecución en la consola (Separación de Responsabilidades - SRP).
/// Desacopla la lógica de visualización del <see cref="Orchestrator"/>.
/// </summary>
public static class ConsoleReportRenderer
{
    /// <summary>
    /// Imprime el banner inicial de bienvenida y configuración de la sesión de ejecución.
    /// </summary>
    public static void PrintStartupBanner(AgentContext context, string taskDescription)
    {
        ArgumentNullException.ThrowIfNull(context);

        AnsiConsole.Write(new Rule("[bold cyan]🚀 DevBot - Motor Agéntico de Desarrollo Autónomo[/]").RuleStyle("cyan"));
        AnsiConsole.MarkupLine($"[grey]Directorio de trabajo:[/] [yellow]{context.RepoRoot}[/]");
        AnsiConsole.MarkupLine($"[grey]Modelo IA:[/] [cyan]{context.ModelId}[/]");
        AnsiConsole.MarkupLine($"[grey]Modo de operación:[/] [bold magenta]{context.ModePolicy.DisplayName}[/]");
        AnsiConsole.MarkupLine($"[grey]Requerimiento:[/] [white]{Markup.Escape(taskDescription)}[/]\n");
    }

    /// <summary>
    /// Imprime el encabezado visual descriptivo de un hito atómico antes de su ejecución.
    /// </summary>
    public static void PrintMilestoneHeader(PlanMilestone milestone, int totalMilestones)
    {
        ArgumentNullException.ThrowIfNull(milestone);

        AnsiConsole.MarkupLine($"\n[bold white on blue] 📍 HITO {milestone.Order} DE {totalMilestones}: {Markup.Escape(milestone.Title)} [/]");
        AnsiConsole.MarkupLine($"[grey]Capa:[/] [magenta]{Markup.Escape(milestone.Scope)}[/] | [grey]Verificación:[/] [green]{Markup.Escape(milestone.VerificationCommand)}[/]");
        if (milestone.TargetFiles.Count > 0)
        {
            AnsiConsole.MarkupLine($"[grey]Archivos objetivo:[/] {string.Join(", ", milestone.TargetFiles.Select(f => $"[yellow]{Markup.Escape(f)}[/]"))}");
        }
        AnsiConsole.MarkupLine($"[grey]Alcance:[/] [cyan]{Markup.Escape(milestone.Description)}[/]\n");
    }

    /// <summary>
    /// Imprime el panel detallado de éxito, fases ejecutadas, métricas y enlaces de Pull Request.
    /// </summary>
    public static void PrintSuccessSummary(AgentContext context, string? diffSummary = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow("[bold green]Estado:[/]", "[green]COMPLETADO CON ÉXITO[/]");
        grid.AddRow("[bold cyan]Rama Local:[/]", $"[cyan]{Markup.Escape(context.TargetBranch)}[/]");
        grid.AddRow("[bold cyan]Rama Remota Publicada:[/]", $"[bold cyan]origin/{Markup.Escape(context.TargetBranch)}[/]");
        grid.AddRow("[bold yellow]Commit Hash:[/]", $"[yellow]{Markup.Escape(context.Metrics.CommitHash ?? string.Empty)}[/]");
        if (!string.IsNullOrEmpty(context.PullRequestUrl))
        {
            grid.AddRow("[bold green]Pull Request:[/]", $"[link={context.PullRequestUrl}][bold underline green]{Markup.Escape(context.PullRequestUrl)}[/][/]");
        }
        grid.AddRow("[bold]Tiempo Transcurrido:[/]", $"{context.Metrics.Elapsed.TotalSeconds:F1}s");
        grid.AddRow("[bold]Reintentos Coder/Reviewer:[/]", $"{context.Metrics.CoderRetries}");
        grid.AddRow("[bold]Tokens Consumidos (estimados):[/]", $"{context.Metrics.TokensConsumed:N0}");

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("[bold green] ✔ RESUMEN DE PULL REQUEST [/]"),
            Border = BoxBorder.Double,
            Padding = new Padding(1, 1, 1, 1)
        };

        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);

        if (context.Metrics.Phases.Count > 0)
        {
            var phaseTable = new Table().Border(TableBorder.Rounded);
            phaseTable.AddColumn(new TableColumn("[bold cyan]Fase / Subagente[/]"));
            phaseTable.AddColumn(new TableColumn("[bold]Duración[/]").Centered());
            phaseTable.AddColumn(new TableColumn("[bold]Tokens[/]").Centered());
            phaseTable.AddColumn(new TableColumn("[bold]Herramientas Invocadas[/]"));
            phaseTable.AddColumn(new TableColumn("[bold]Estado[/]").Centered());

            foreach (var p in context.Metrics.Phases)
            {
                string tools = p.ToolCalls.Count > 0
                    ? string.Join(", ", p.ToolCalls.Select(tc => $"{tc.Key} ({tc.Value})"))
                    : "-";
                string status = p.Success ? "[green]✔ OK[/]" : "[yellow]⚠ Alerta[/]";
                phaseTable.AddRow(p.PhaseName, $"{p.Duration.TotalSeconds:F1}s", $"{p.TotalTokens:N0}", tools, status);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(phaseTable);
        }

        if (!string.IsNullOrEmpty(context.PullRequestUrl))
        {
            AnsiConsole.MarkupLine($"\n[bold yellow]👉 Pull Request listo para auditoría humana:[/] [link={context.PullRequestUrl}][bold underline green]{Markup.Escape(context.PullRequestUrl)}[/][/]");
            AnsiConsole.MarkupLine("[dim]   (Por política de seguridad, el Pull Request requiere revisión y aprobación manual. Sin automerge.)[/]\n");
        }

        AnsiConsole.MarkupLine($"[bold cyan]📄 Informe Ejecutivo Markdown:[/] [yellow]{Markup.Escape(context.LatestReportPath)}[/]");
        AnsiConsole.MarkupLine($"[bold grey]📜 Registro de Auditoría (Log):[/] [dim]{Markup.Escape(context.Logger.LatestLogPath)}[/]\n");

        if (!string.IsNullOrWhiteSpace(diffSummary))
        {
            var diffPanel = new Panel(new Text(diffSummary))
            {
                Header = new PanelHeader("[bold cyan] Archivos Modificados (git diff --stat) [/]"),
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(diffPanel);
        }
    }

    /// <summary>
    /// Imprime el resumen de fallo o reversión cuando una ejecución no alcanza el criterio verde.
    /// </summary>
    public static void PrintFailureSummary(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow("[bold red]Estado:[/]", "[red]FALLIDO / REVERTIDO[/]");
        grid.AddRow("[bold]Motivo:[/]", $"[red]{Markup.Escape(context.Metrics.ErrorMessage ?? string.Empty)}[/]");
        grid.AddRow("[bold]Tiempo Transcurrido:[/]", $"{context.Metrics.Elapsed.TotalSeconds:F1}s");
        grid.AddRow("[bold]Reintentos Realizados:[/]", $"{context.Metrics.CoderRetries}");
        grid.AddRow("[bold]Tokens Consumidos:[/]", $"{context.Metrics.TokensConsumed:N0}");

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("[bold red] ❌ RESUMEN DE EJECUCIÓN (ROLLBACK) [/]"),
            Border = BoxBorder.Heavy,
            Padding = new Padding(1, 1, 1, 1)
        };

        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);

        if (context.Metrics.Phases.Count > 0)
        {
            var phaseTable = new Table().Border(TableBorder.Rounded);
            phaseTable.AddColumn(new TableColumn("[bold cyan]Fase / Subagente[/]"));
            phaseTable.AddColumn(new TableColumn("[bold]Duración[/]").Centered());
            phaseTable.AddColumn(new TableColumn("[bold]Tokens[/]").Centered());
            phaseTable.AddColumn(new TableColumn("[bold]Herramientas Invocadas[/]"));
            phaseTable.AddColumn(new TableColumn("[bold]Estado[/]").Centered());

            foreach (var p in context.Metrics.Phases)
            {
                string tools = p.ToolCalls.Count > 0
                    ? string.Join(", ", p.ToolCalls.Select(tc => $"{tc.Key} ({tc.Value})"))
                    : "-";
                string status = p.Success ? "[green]✔ OK[/]" : "[red]❌ Error[/]";
                phaseTable.AddRow(p.PhaseName, $"{p.Duration.TotalSeconds:F1}s", $"{p.TotalTokens:N0}", tools, status);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(phaseTable);
        }

        AnsiConsole.MarkupLine($"\n[bold cyan]📄 Informe Ejecutivo del Fallo:[/] [yellow]{Markup.Escape(context.LatestReportPath)}[/]");
        AnsiConsole.MarkupLine($"[bold grey]📜 Registro de Auditoría (Log):[/] [dim]{Markup.Escape(context.Logger.LatestLogPath)}[/]\n");
    }
}
