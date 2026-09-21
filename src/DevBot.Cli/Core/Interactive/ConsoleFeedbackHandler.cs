using Spectre.Console;

namespace DevBot.Cli.Core.Interactive;

/// <summary>
/// Implementación interactiva en consola con Spectre.Console para la interacción Human-in-the-Loop.
/// Permite al desarrollador auditar y autorizar el plan de ejecución o inyectar directivas al Coder.
/// </summary>
public class ConsoleFeedbackHandler : IHumanFeedbackHandler
{
    /// <summary>
    /// Solicita al desarrollador la revisión y aprobación del plan de ejecución estructurado en hitos.
    /// </summary>
    /// <param name="plan">Resumen integral del plan con hitos y comandos de verificación.</param>
    /// <param name="cancellationToken">Token de cancelación cooperativa.</param>
    /// <returns>Decisión tomada por el desarrollador.</returns>
    public Task<HumanDecision> RequestPlanApprovalAsync(
        PlanApprovalSummary plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold yellow]🎯 Human-in-the-Loop: Revisión y Aprobación del Plan de Ejecución[/]").RuleStyle("yellow"));

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold cyan]Atributo[/]").Width(24));
        table.AddColumn(new TableColumn("[bold]Detalle del Plan[/]"));

        table.AddRow("[bold]Requerimiento[/]", Markup.Escape(plan.TaskDescription));
        table.AddRow("[bold]Modo de Operación[/]", $"[magenta]{plan.Mode}[/]");
        table.AddRow("[bold]Stack Detectado[/]", $"[green]{plan.Stack.StackType}[/] ({plan.Stack.Language} vía {plan.Stack.BuildTool})");
        table.AddRow("[bold]Estrategia[/]", $"[cyan]{plan.StrategySummary}[/]");
        table.AddRow("[bold]Total de Hitos[/]", $"[yellow]{plan.Plan.Milestones.Count} hitos atómicos[/]");

        string filesText = plan.TargetFiles.Count > 0
            ? string.Join("\n", plan.TargetFiles.Select(f => $"[yellow]• {Markup.Escape(f)}[/]"))
            : "[dim](Identificados dinámicamente durante la ejecución de hitos)[/]";

        table.AddRow("[bold]Archivos Involucrados[/]", filesText);

        AnsiConsole.Write(table);

        if (!string.IsNullOrWhiteSpace(plan.ArchitecturalSummary))
        {
            var previewPanel = new Panel(new Text(plan.ArchitecturalSummary))
            {
                Header = new PanelHeader("[bold cyan] Resumen de Arquitectura [/]"),
                Border = BoxBorder.Rounded,
                Padding = new Padding(1, 1, 1, 1)
            };
            AnsiConsole.Write(previewPanel);
            AnsiConsole.WriteLine();
        }

        const string optApprove = "[green]1. Aprobar plan y proceder a la codificación quirúrgica[/]";
        const string optClarify = "[yellow]2. Agregar aclaraciones / guiar al Coder antes de editar[/]";
        const string optAbort = "[red]3. Abortar misión y descartar cambios[/]";

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold white]¿Cómo deseas proceder con el plan de hitos propuesto?[/]")
                .AddChoices(optApprove, optClarify, optAbort)
        );

        if (choice == optApprove)
        {
            AnsiConsole.MarkupLine("[bold green]✔ Plan de ejecución aprobado por el desarrollador. Iniciando CoderAgent...[/]\n");
            return Task.FromResult(new HumanDecision(HumanDecisionType.Approved));
        }

        if (choice == optClarify)
        {
            string guidance = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold yellow]Ingresa tus instrucciones o directivas adicionales para el CoderAgent:[/]")
                    .PromptStyle("cyan")
                    .ValidationErrorMessage("[red]Las aclaraciones no pueden estar vacías.[/]")
                    .Validate(text => !string.IsNullOrWhiteSpace(text))
            );

            AnsiConsole.MarkupLine("[bold green]✔ Aclaraciones registradas. Inyectando directivas al CoderAgent...[/]\n");
            return Task.FromResult(new HumanDecision(HumanDecisionType.Clarified, guidance.Trim()));
        }

        AnsiConsole.MarkupLine("[bold red]❌ Misión cancelada por el desarrollador.[/]\n");
        return Task.FromResult(new HumanDecision(HumanDecisionType.Aborted));
    }

    /// <summary>
    /// Método de compatibilidad para revisión preliminar del diagnóstico Scout.
    /// </summary>
    public Task<HumanDecision> RequestScoutApprovalAsync(
        ScoutPlanSummary plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold yellow]🎯 Human-in-the-Loop: Revisión de Diagnóstico Scout[/]").RuleStyle("yellow"));

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold cyan]Atributo[/]").Width(22));
        table.AddColumn(new TableColumn("[bold]Detalle Diagnóstico[/]"));

        table.AddRow("[bold]Requerimiento[/]", Markup.Escape(plan.TaskDescription));
        table.AddRow("[bold]Modo de Operación[/]", $"[magenta]{plan.Mode}[/]");
        table.AddRow("[bold]Stack Detectado[/]", $"[green]{plan.Stack.StackType}[/] ({plan.Stack.Language} vía {plan.Stack.BuildTool})");
        table.AddRow("[bold]Estrategia[/]", $"[cyan]{plan.StrategySummary}[/]");

        string filesText = plan.TargetFiles.Count > 0
            ? string.Join("\n", plan.TargetFiles.Select(f => $"[yellow]• {Markup.Escape(f)}[/]"))
            : "[dim](Identificados dinámicamente en scout_report.md)[/]";

        table.AddRow("[bold]Archivos a Modificar[/]", filesText);

        AnsiConsole.Write(table);

        var previewPanel = new Panel(new Text(plan.ScoutReportSnippet))
        {
            Header = new PanelHeader("[bold cyan] Resumen del Plan Técnico (scout_report.md) [/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 1, 1, 1)
        };
        AnsiConsole.Write(previewPanel);
        AnsiConsole.WriteLine();

        const string optApprove = "[green]1. Aprobar plan y proceder a la codificación quirúrgica[/]";
        const string optClarify = "[yellow]2. Agregar aclaraciones / guiar al Coder antes de editar[/]";
        const string optAbort = "[red]3. Abortar misión y descartar cambios[/]";

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold white]¿Cómo deseas proceder con el plan propuesto?[/]")
                .AddChoices(optApprove, optClarify, optAbort)
        );

        if (choice == optApprove)
        {
            AnsiConsole.MarkupLine("[bold green]✔ Plan aprobado por el desarrollador. Continuando...[/]\n");
            return Task.FromResult(new HumanDecision(HumanDecisionType.Approved));
        }

        if (choice == optClarify)
        {
            string guidance = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold yellow]Ingresa tus instrucciones o directivas adicionales para el CoderAgent:[/]")
                    .PromptStyle("cyan")
                    .ValidationErrorMessage("[red]Las aclaraciones no pueden estar vacías.[/]")
                    .Validate(text => !string.IsNullOrWhiteSpace(text))
            );

            AnsiConsole.MarkupLine("[bold green]✔ Aclaraciones registradas. Inyectando contexto al CoderAgent...[/]\n");
            return Task.FromResult(new HumanDecision(HumanDecisionType.Clarified, guidance.Trim()));
        }

        AnsiConsole.MarkupLine("[bold red]❌ Misión cancelada por el desarrollador.[/]\n");
        return Task.FromResult(new HumanDecision(HumanDecisionType.Aborted));
    }
}
