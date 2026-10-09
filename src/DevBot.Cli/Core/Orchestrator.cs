using DevBot.Cli.Core.Auditing;
using DevBot.Cli.Core.Interactive;
using DevBot.Cli.Core.Memory;
using DevBot.Cli.Core.Modes;
using DevBot.Cli.Core.Planning;
using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Subagents;
using DevBot.Cli.Tools;
using Spectre.Console;

namespace DevBot.Cli.Core;

/// <summary>
/// Orquestador central del ciclo de vida del agente autónomo.
/// Coordina la exploración (Scout), planificación (Planner), codificación quirúrgica (Coder),
/// verificación determinista (Reviewer), auditoría de seguridad y publicación en Git/GitHub.
/// </summary>
public class Orchestrator : IOrchestrator
{
    private readonly AgentContext _context;
    private readonly IGitTools _gitTools;
    private readonly IScoutAgent _scoutAgent;
    private readonly IPlannerAgent _plannerAgent;
    private readonly ICoderAgent _coderAgent;
    private readonly IReviewerAgent _reviewerAgent;
    private readonly IProjectStackDetector _stackDetector;

    /// <summary>
    /// Inicializa una nueva instancia de <see cref="Orchestrator"/> inyectando los subagentes y servicios necesarios.
    /// </summary>
    /// <param name="context">Contexto global de ejecución del agente.</param>
    /// <param name="scoutAgent">Agente de exploración arquitectónica.</param>
    /// <param name="plannerAgent">Agente de descomposición jerárquica.</param>
    /// <param name="coderAgent">Agente de implementación de código.</param>
    /// <param name="reviewerAgent">Agente de aseguramiento de calidad y pruebas.</param>
    /// <param name="stackDetector">Detector de tecnologías del proyecto (opcional).</param>
    /// <param name="gitTools">Herramientas de control de versiones Git (opcional).</param>
    public Orchestrator(
        AgentContext context,
        IScoutAgent scoutAgent,
        IPlannerAgent plannerAgent,
        ICoderAgent coderAgent,
        IReviewerAgent reviewerAgent,
        IProjectStackDetector? stackDetector = null,
        IGitTools? gitTools = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scoutAgent);
        ArgumentNullException.ThrowIfNull(plannerAgent);
        ArgumentNullException.ThrowIfNull(coderAgent);
        ArgumentNullException.ThrowIfNull(reviewerAgent);

        _context = context;
        _scoutAgent = scoutAgent;
        _plannerAgent = plannerAgent;
        _coderAgent = coderAgent;
        _reviewerAgent = reviewerAgent;
        _stackDetector = stackDetector ?? new ProjectStackDetector();
        _gitTools = gitTools ?? new GitTools(context.RepoRoot);
    }

    /// <summary>
    /// Ejecuta el pipeline agéntico completo para la tarea dada.
    /// </summary>
    /// <param name="taskDescription">Descripción del requerimiento solicitado.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Verdadero si la misión se completó con éxito; de lo contrario, falso.</returns>
    public async Task<bool> RunAsync(string taskDescription, CancellationToken cancellationToken = default)
    {
        _context.Metrics.Start();
        ConsoleReportRenderer.PrintStartupBanner(_context, taskDescription);
        _context.Logger.LogEvent("Orchestrator", "ModeConfig", $"Modo: {_context.ModePolicy.DisplayName}");

        if (!await InitializeGitWorkspaceAsync(taskDescription, cancellationToken))
        {
            return false;
        }

        await DetectStackAndRulesAsync(cancellationToken);

        if (!await ExecuteScoutPhaseAsync(cancellationToken))
        {
            return false;
        }

        if (!await ExecutePlannerPhaseAsync(cancellationToken))
        {
            return false;
        }

        if (!await ConfirmExecutionPlanAsync(cancellationToken))
        {
            return false;
        }

        if (!await ExecuteMilestonesLoopAsync(cancellationToken))
        {
            return false;
        }

        await PublishPullRequestAsync(taskDescription, cancellationToken);
        await FinalizeRunAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ResumeAsync(CancellationToken cancellationToken = default)
    {
        _context.Metrics.Start();

        var plan = await _context.ReadPlanAsync(cancellationToken);
        if (plan == null || plan.Milestones.Count == 0)
        {
            AnsiConsole.MarkupLine("[bold red]❌ No se encontró ningún plan previo en .agent/plan.json para reanudar.[/]");
            _context.Metrics.Stop();
            _context.Metrics.ErrorMessage = "No pending plan found in .agent/plan.json.";
            return false;
        }

        _context.Plan = plan;
        string savedTask = await _context.ReadTaskAsync(cancellationToken);
        _context.TaskDescription = !string.IsNullOrWhiteSpace(savedTask) ? savedTask : plan.TaskDescription;

        ConsoleReportRenderer.PrintStartupBanner(_context, $"[REANUDACIÓN] {_context.TaskDescription}");
        _context.Logger.LogEvent("Orchestrator", "ResumeExecution", $"Reanudando ejecución para: {_context.TaskDescription}");

        bool isGit = await _gitTools.IsGitRepositoryAsync(cancellationToken);
        if (!isGit)
        {
            AnsiConsole.MarkupLine("[bold red]❌ El directorio especificado no es un repositorio Git válido.[/]");
            _context.Metrics.Stop();
            _context.Metrics.ErrorMessage = "Not a git repository.";
            return false;
        }

        _context.TargetBranch = await _gitTools.GetCurrentBranchAsync(cancellationToken);
        _context.OriginalBranch = _context.TargetBranch;
        _gitTools.EnsureGitExclude(".agent/");

        await DetectStackAndRulesAsync(cancellationToken);

        int startIndex = ResumableExecutionService.GetResumeStartIndex(plan);
        if (startIndex == -1)
        {
            AnsiConsole.MarkupLine("[bold green]✔ Todos los hitos del plan ya fueron completados previamente.[/]");
            await PublishPullRequestAsync(_context.TaskDescription, cancellationToken);
            await FinalizeRunAsync(cancellationToken);
            return true;
        }

        AnsiConsole.MarkupLine($"[bold cyan]🔁 [[Orchestrator]][/] Reanudando desde Hito #{plan.Milestones[startIndex].Order} ('{Markup.Escape(plan.Milestones[startIndex].Title)}') ({startIndex} de {plan.Milestones.Count} ya completados).");

        if (!await ExecuteMilestonesLoopAsync(cancellationToken, startIndex))
        {
            return false;
        }

        await PublishPullRequestAsync(_context.TaskDescription, cancellationToken);
        await FinalizeRunAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> PlanOnlyAsync(string taskDescription, CancellationToken cancellationToken = default)
    {
        _context.Metrics.Start();
        ConsoleReportRenderer.PrintStartupBanner(_context, $"[DRY-RUN] {taskDescription}");
        _context.Logger.LogEvent("Orchestrator", "DryRun", $"Ejecución Dry-Run (Solo Planificación): {taskDescription}");

        _gitTools.EnsureGitExclude(".agent/");
        await _context.SaveTaskAsync(taskDescription, cancellationToken);

        await DetectStackAndRulesAsync(cancellationToken);

        if (!await ExecuteScoutPhaseAsync(cancellationToken))
        {
            return false;
        }

        if (!await ExecutePlannerPhaseAsync(cancellationToken))
        {
            return false;
        }

        if (!await ConfirmExecutionPlanAsync(cancellationToken))
        {
            return false;
        }

        AnsiConsole.MarkupLine("\n[bold green]✔ Modo --dry-run finalizado con éxito.[/] El plan arquitectónico ha sido generado y persistido en [yellow].agent/plan.json[/].");
        AnsiConsole.MarkupLine("[dim]Para ejecutar este plan posteriormente, corre:[/] [bold cyan]devbot --continue[/]\n");

        _context.Metrics.Stop();
        return true;
    }

    private async Task<bool> InitializeGitWorkspaceAsync(string taskDescription, CancellationToken cancellationToken)
    {
        bool isGit = await _gitTools.IsGitRepositoryAsync(cancellationToken);
        if (!isGit)
        {
            AnsiConsole.MarkupLine("[bold red]❌ El directorio especificado no es un repositorio Git válido.[/]");
            _context.Metrics.Stop();
            _context.Metrics.ErrorMessage = "Not a git repository.";
            return false;
        }

        _context.OriginalBranch = await _gitTools.GetCurrentBranchAsync(cancellationToken);
        string branchSlug = GitTools.CreateSlug(taskDescription);
        string uniqueSuffix = DateTime.UtcNow.ToString("MMdd-HHmm");
        _context.TargetBranch = $"feature/{branchSlug}-{uniqueSuffix}";

        AnsiConsole.MarkupLine($"[grey]Rama original:[/] [dim]{_context.OriginalBranch}[/]");
        AnsiConsole.MarkupLine($"[grey]Creando rama de trabajo:[/] [green]{_context.TargetBranch}[/]");

        string branchResult = await _gitTools.CheckoutNewBranch(_context.TargetBranch, cancellationToken);
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(branchResult)}[/]\n");

        _gitTools.EnsureGitExclude(".agent/");
        await _context.SaveTaskAsync(taskDescription, cancellationToken);
        AnsiConsole.MarkupLine($"[bold blue]📁 [[Orchestrator]][/] Requerimiento guardado en [yellow]{Markup.Escape(_context.TaskFilePath)}[/]");
        return true;
    }

    private async Task DetectStackAndRulesAsync(CancellationToken cancellationToken)
    {
        _context.StackInfo = await _stackDetector.DetectAsync(_context.RepoRoot, cancellationToken);
        _context.Strategy = StrategyResolver.Resolve(_context.StackInfo);

        AnsiConsole.MarkupLine($"[grey]Stack detectado:[/] [bold green]{_context.StackInfo.StackType}[/] ([cyan]{_context.StackInfo.Language}[/] vía [yellow]{_context.StackInfo.BuildTool}[/])");
        _context.Logger.LogEvent("Orchestrator", "StackDetection", $"Stack: {_context.StackInfo.StackType}, Language: {_context.StackInfo.Language}, Strategy: {_context.Strategy.Name}");

        _context.LocalRules = await _context.MemoryService.LoadLocalRulesAsync(_context.RepoRoot, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_context.LocalRules))
        {
            AnsiConsole.MarkupLine("[bold cyan]📜 [[Orchestrator]][/] Reglas locales del equipo detectadas (.devbotrules). Inyectando directrices.");
            _context.Logger.LogEvent("Orchestrator", "LocalRulesLoaded", "Reglas locales .devbotrules cargadas con éxito.");
        }

        string originalCommitHash = await _gitTools.GetLastCommitHashAsync(cancellationToken);
        var cachedMap = await _context.MemoryService.LoadRepoMapAsync(_context.RepoRoot, cancellationToken);
        if (cachedMap != null && cachedMap.LastCommitHash == originalCommitHash)
        {
            _context.RepoMap = cachedMap;
            AnsiConsole.MarkupLine($"[grey]Mapa de arquitectura cargado desde memoria persistente (.devbot/repo_map.json - {cachedMap.Components.Count} componentes).[/]");
            _context.Logger.LogEvent("Orchestrator", "RepoMapLoaded", $"Mapa en caché ({cachedMap.LastCommitHash})");
        }
        else
        {
            _context.RepoMap = await _context.MemoryService.GenerateRepoMapAsync(_context.RepoRoot, _context.StackInfo, originalCommitHash, cancellationToken);
            await _context.MemoryService.SaveRepoMapAsync(_context.RepoRoot, _context.RepoMap, cancellationToken);
            _context.Logger.LogEvent("Orchestrator", "RepoMapGenerated", $"Nuevo mapa de arquitectura generado ({_context.RepoMap.Components.Count} componentes).");
        }
    }

    private async Task<bool> ExecuteScoutPhaseAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new Rule("[bold cyan]Fase 1: Exploración y Análisis de Arquitectura[/]").RuleStyle("cyan"));
        _context.Logger.LogEvent("Orchestrator", "PhaseStart", "Iniciando Fase 1: Scout Agent");
        _context.Metrics.StartPhase("Scout Agent");

        bool scoutOk = false;
        try
        {
            scoutOk = await _scoutAgent.ExecuteAsync(_context, cancellationToken);
        }
        catch (Exception ex)
        {
            _context.Logger.LogEvent("Orchestrator", "ScoutException", ex.Message);
            scoutOk = false;
        }

        _context.Metrics.EndPhase(scoutOk, scoutOk ? "Exploración completada" : "Fallo en exploración");
        _context.Logger.LogEvent("Orchestrator", "PhaseEnd", $"Fase 1 finalizada: {(scoutOk ? "OK" : "FAILED")}");

        if (!scoutOk)
        {
            AnsiConsole.MarkupLine("[bold red]❌ [[Orchestrator]][/] La fase Scout falló. Abortando misión.");
            await AbortAndCleanupAsync("Fallo durante la fase Scout", cancellationToken);
            return false;
        }

        return true;
    }

    private async Task<bool> ExecutePlannerPhaseAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new Rule("[bold cyan]Fase 2: Planificación y Descomposición en Hitos[/]").RuleStyle("cyan"));
        _context.Logger.LogEvent("Orchestrator", "PhaseStart", "Iniciando Fase 2: Planner Agent");
        _context.Metrics.StartPhase("Planner Agent");

        bool plannerOk = false;
        try
        {
            plannerOk = await _plannerAgent.ExecuteAsync(_context, cancellationToken);
        }
        catch (Exception ex)
        {
            _context.Logger.LogEvent("Orchestrator", "PlannerException", ex.Message);
            plannerOk = false;
        }

        _context.Metrics.EndPhase(plannerOk, plannerOk ? "Plan generado" : "Fallo en planificación");
        _context.Logger.LogEvent("Orchestrator", "PhaseEnd", $"Fase 2 finalizada: {(plannerOk ? "OK" : "FAILED")}");

        if (!plannerOk || _context.Plan == null || _context.Plan.Milestones.Count == 0)
        {
            AnsiConsole.MarkupLine("[bold red]❌ [[Orchestrator]][/] La fase Planner falló. Abortando misión.");
            await AbortAndCleanupAsync("Fallo durante la fase de planificación.", cancellationToken);
            return false;
        }

        return true;
    }

    private async Task<bool> ConfirmExecutionPlanAsync(CancellationToken cancellationToken)
    {
        if (_context.AutoApprove)
        {
            AnsiConsole.MarkupLine("[dim grey]⚡ [[Orchestrator]][/] Flag --yes activo: Omitiendo confirmación interactiva del plan de ejecución.[/]");
            return true;
        }

        if (_context.Plan == null)
        {
            AnsiConsole.MarkupLine("[bold red]❌ [[Orchestrator]][/] No existe un plan estructurado para confirmar. Abortando misión.");
            await AbortAndCleanupAsync("Plan nulo previo a la confirmación", cancellationToken);
            return false;
        }

        var targetFiles = _context.Plan.Milestones
            .SelectMany(m => m.TargetFiles)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetFiles.Count == 0)
        {
            string scoutReport = await _context.ReadScoutReportAsync(cancellationToken);
            targetFiles = ScoutPlanExtractor.ExtractTargetFiles(scoutReport).ToList();
        }

        var planApprovalSummary = new PlanApprovalSummary(
            _context.TaskDescription,
            _context.Mode,
            _context.StackInfo,
            _context.Strategy.Name,
            _context.Plan,
            targetFiles,
            _context.Plan.ArchitecturalSummary
        );

        var decision = await _context.FeedbackHandler.RequestPlanApprovalAsync(planApprovalSummary, cancellationToken);
        if (decision.Type == HumanDecisionType.Aborted)
        {
            await AbortAndCleanupAsync("Misión abortada por el desarrollador tras revisar el plan de ejecución.", cancellationToken);
            return false;
        }

        if (decision.Type == HumanDecisionType.Clarified && !string.IsNullOrWhiteSpace(decision.AdditionalGuidance))
        {
            _context.AdditionalUserGuidance = decision.AdditionalGuidance;
            _context.Logger.LogEvent("Orchestrator", "HumanFeedback", $"Directivas del desarrollador para el Coder: {decision.AdditionalGuidance}");
        }

        return true;
    }

    private async Task<bool> ExecuteMilestonesLoopAsync(CancellationToken cancellationToken, int startFromIndex = 0)
    {
        AnsiConsole.Write(new Rule("[bold yellow]Fase 3: Bucle Quirúrgico de Hitos y Checkpoints[/]").RuleStyle("yellow"));
        string lastCheckpointCommitHash = await _gitTools.GetLastCommitHashAsync(cancellationToken);
        int totalMilestones = _context.Plan!.Milestones.Count;

        for (int mIdx = startFromIndex; mIdx < totalMilestones; mIdx++)
        {
            var milestone = _context.Plan.Milestones[mIdx];
            _context.CurrentMilestone = milestone;
            _context.MilestoneModifiedFiles.Clear();

            ConsoleReportRenderer.PrintMilestoneHeader(milestone, totalMilestones);

            if (_context.InteractivePerMilestone && !_context.AutoApprove)
            {
                var decision = await _context.FeedbackHandler.RequestMilestoneApprovalAsync(milestone, cancellationToken);
                if (decision.Type == Interactive.HumanDecisionType.Aborted)
                {
                    await AbortAndCleanupAsync($"Ejecución abortada por el desarrollador en el Hito #{milestone.Order}.", cancellationToken);
                    return false;
                }

                if (decision.AdditionalGuidance == "SKIP_MILESTONE")
                {
                    AnsiConsole.MarkupLine($"[yellow]⏭ Hito #{milestone.Order} saltado por decisión del desarrollador.[/]");
                    continue;
                }

                if (decision.Type == Interactive.HumanDecisionType.Clarified && !string.IsNullOrWhiteSpace(decision.AdditionalGuidance))
                {
                    _context.AdditionalUserGuidance = decision.AdditionalGuidance;
                    _context.Logger.LogEvent("Orchestrator", "HumanFeedback", $"Directivas del desarrollador para el Hito #{milestone.Order}: {decision.AdditionalGuidance}");
                }
            }

            bool testsPassed = await ExecuteMilestoneAttemptsAsync(milestone, cancellationToken);
            if (!testsPassed)
            {
                AnsiConsole.MarkupLine($"[bold red]❌ [[Orchestrator]][/] El Hito #{milestone.Order} ('{Markup.Escape(milestone.Title)}') falló tras {_context.MaxRetries} intentos.");
                AnsiConsole.MarkupLine($"[yellow]🔄 Realizando rollback al último checkpoint verde ({lastCheckpointCommitHash})...[/]");
                await _gitTools.ResetHardToCheckpointAsync(lastCheckpointCommitHash, cancellationToken);
                await AbortAndCleanupAsync($"Fallo en el Hito #{milestone.Order} tras {_context.MaxRetries} intentos.", cancellationToken);
                return false;
            }

            if (!await AuditMilestoneChangesAsync(milestone, lastCheckpointCommitHash, cancellationToken))
            {
                return false;
            }

            string newCommitHash = await CommitMilestoneCheckpointAsync(milestone, lastCheckpointCommitHash, cancellationToken);
            if (string.IsNullOrEmpty(newCommitHash))
            {
                return false;
            }

            lastCheckpointCommitHash = newCommitHash;
        }

        return true;
    }

    private async Task<bool> ExecuteMilestoneAttemptsAsync(PlanMilestone milestone, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= _context.MaxRetries; attempt++)
        {
            AnsiConsole.MarkupLine($"[dim]─── Hito #{milestone.Order} | Intento #{attempt} / {_context.MaxRetries} ───[/]");

            string coderPhase = $"Coder (Hito {milestone.Order}, Iter #{attempt})";
            _context.Logger.LogEvent("Orchestrator", "PhaseStart", $"Iniciando {coderPhase}");
            _context.Metrics.StartPhase(coderPhase);
            bool coderOk = await _coderAgent.ExecuteAsync(_context, cancellationToken);
            _context.Metrics.EndPhase(coderOk);
            _context.Logger.LogEvent("Orchestrator", "PhaseEnd", $"{coderPhase} finalizado: {(coderOk ? "OK" : "ERROR")}");
            if (!coderOk)
            {
                AnsiConsole.MarkupLine("[bold red]❌ [[Coder Agent]][/] Error ejecutando cambios de código en el hito.");
            }

            string reviewerPhase = $"Reviewer (Hito {milestone.Order}, Iter #{attempt})";
            _context.Logger.LogEvent("Orchestrator", "PhaseStart", $"Iniciando {reviewerPhase}");
            _context.Metrics.StartPhase(reviewerPhase);
            bool reviewerPassed = await _reviewerAgent.ExecuteAsync(_context, cancellationToken);
            _context.Metrics.EndPhase(reviewerPassed);
            _context.Logger.LogEvent("Orchestrator", "PhaseEnd", $"{reviewerPhase} finalizado: {(reviewerPassed ? "ALL_PASS" : "FAILURES_DETECTED")}");

            if (reviewerPassed)
            {
                AnsiConsole.MarkupLine($"[bold green]🎉 [[Orchestrator]][/] ¡Hito #{milestone.Order} verificado exitosamente en el intento #{attempt}!");
                return true;
            }

            if (attempt < _context.MaxRetries)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ Las pruebas del hito no pasaron. Retroalimentando resultados a CoderAgent para reintento #{attempt + 1}...[/]");
            }
        }

        return false;
    }

    private async Task<bool> AuditMilestoneChangesAsync(PlanMilestone milestone, string rollbackCommitHash, CancellationToken cancellationToken)
    {
        if (_context.ModifiedFiles.Count == 0)
        {
            return true;
        }

        AnsiConsole.MarkupLine($"[bold cyan]🛡 [[Auditor]][/] Auditando seguridad pre-checkpoint para el Hito #{milestone.Order}...");
        var terminal = new TerminalTools(_context.RepoRoot, _context);
        var auditResult = await _context.Auditor.AuditAsync(
            _context.RepoRoot,
            _context.ModifiedFiles,
            _context.Strategy,
            terminal,
            cancellationToken
        );

        if (!auditResult.Passed)
        {
            AnsiConsole.MarkupLine($"[bold red]❌ [[Auditor]][/] {Markup.Escape(auditResult.Summary)}");
            if (auditResult.SecurityFindings.Count > 0)
            {
                var findingsTable = new Table().Border(TableBorder.Heavy);
                findingsTable.AddColumn(new TableColumn("[bold red]Archivo:Línea[/]"));
                findingsTable.AddColumn(new TableColumn("[bold yellow]Regla[/]"));
                findingsTable.AddColumn(new TableColumn("[bold]Descripción[/]"));
                findingsTable.AddColumn(new TableColumn("[bold grey]Fragmento Redactado[/]"));

                foreach (var f in auditResult.SecurityFindings)
                {
                    findingsTable.AddRow(
                        $"{Markup.Escape(f.FilePath)}:{f.LineNumber}",
                        f.RuleId,
                        Markup.Escape(f.Description),
                        Markup.Escape(f.RedactedSnippet)
                    );
                }
                AnsiConsole.Write(findingsTable);
            }

            _context.Logger.LogEvent("Orchestrator", "AuditFailed", auditResult.Summary);
            AnsiConsole.MarkupLine($"[yellow]🔄 Realizando rollback al último checkpoint verde ({rollbackCommitHash})...[/]");
            await _gitTools.ResetHardToCheckpointAsync(rollbackCommitHash, cancellationToken);
            await AbortAndCleanupAsync(auditResult.Summary, cancellationToken);
            return false;
        }

        AnsiConsole.MarkupLine($"[bold green]✔ [[Auditor]][/] Auditoría de seguridad aprobada para Hito #{milestone.Order}.");
        return true;
    }

    private async Task<string> CommitMilestoneCheckpointAsync(PlanMilestone milestone, string rollbackCommitHash, CancellationToken cancellationToken)
    {
        string commitPrefix = _context.Mode switch
        {
            AgentMode.Bug => "fix",
            AgentMode.Refactor => "refactor",
            AgentMode.Test => "test",
            _ => "feat"
        };
        string scopeStr = !string.IsNullOrWhiteSpace(milestone.Scope) ? $"({milestone.Scope})" : string.Empty;
        string checkpointMsg = $"{commitPrefix}{scopeStr}: {milestone.Title}";

        string commitResult = await _gitTools.Commit(checkpointMsg, cancellationToken);
        if (commitResult.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[bold red]❌ Error creando commit de checkpoint:[/] {Markup.Escape(commitResult)}");
            await _gitTools.ResetHardToCheckpointAsync(rollbackCommitHash, cancellationToken);
            await AbortAndCleanupAsync(commitResult, cancellationToken);
            return string.Empty;
        }

        milestone.IsCompleted = true;
        string newCommitHash = await _gitTools.GetLastCommitHashAsync(cancellationToken);
        milestone.CheckpointCommitHash = newCommitHash;
        AnsiConsole.MarkupLine($"[bold green]💾 Checkpoint guardado:[/] [cyan]{newCommitHash}[/] [dim]({checkpointMsg})[/]");
        await _context.SavePlanAsync(_context.Plan!, cancellationToken);
        return newCommitHash;
    }

    private async Task PublishPullRequestAsync(string taskDescription, CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new Rule("[bold green]Fase 4: Publicación Remota y Apertura de Pull Request[/]").RuleStyle("green"));

        AnsiConsole.MarkupLine($"[cyan]🚀 Publicando rama remota en origin/{Markup.Escape(_context.TargetBranch)}...[/]");
        var pushResult = await _gitTools.PushBranch(_context.TargetBranch, cancellationToken);
        if (pushResult.Success)
        {
            AnsiConsole.MarkupLine($"[bold green]✔ Rama remota publicada exitosamente: origin/{Markup.Escape(_context.TargetBranch)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ No se pudo hacer push a origin:[/] [dim]{Markup.Escape(pushResult.Error)}[/]");
        }

        string prBaseBranch = !string.IsNullOrWhiteSpace(_context.OriginalBranch) ? _context.OriginalBranch : "main";
        string prTitle = $"feat: {taskDescription}";

        var milestonesList = _context.Plan != null && _context.Plan.Milestones.Count > 0
            ? string.Join("\n", _context.Plan.Milestones.Select(m => $"- [x] `{m.CheckpointCommitHash}` **{m.Title}** (`{m.Scope}`)"))
            : "- [x] Implementación completada";

        string prBody = $"## Resumen de Cambios\n\nRequerimiento: {taskDescription}\n\n" +
                        $"### Hitos Ejecutados ({_context.Plan?.Milestones.Count ?? 1})\n" +
                        milestonesList +
                        "\n\n### Archivos Modificados Quirúrgicamente\n" +
                        string.Join("\n", _context.ModifiedFiles.Select(f => $"- `{f}`")) +
                        "\n\n---\n*Generado automáticamente por DevBot con Descomposición Jerárquica y Checkpoints.*";

        AnsiConsole.MarkupLine("[cyan]🔍 Generando Pull Request (esperando exclusivamente revisión y aprobación manual)...[/]");
        var prResult = await _gitTools.CreatePullRequestAsync(_context.TargetBranch, prBaseBranch, prTitle, prBody, cancellationToken);
        if (prResult.Success)
        {
            _context.PullRequestUrl = prResult.PrUrl;
            AnsiConsole.MarkupLine($"[bold green]✔ {Markup.Escape(prResult.Message)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ {Markup.Escape(prResult.Message)}[/]");
        }
    }

    private async Task FinalizeRunAsync(CancellationToken cancellationToken)
    {
        string diffSummary = await _gitTools.GetDiffSummaryAsync(cancellationToken);
        _context.Metrics.CommitHash = await _gitTools.GetLastCommitHashAsync(cancellationToken);
        _context.Metrics.Success = true;
        _context.Metrics.Stop();

        if (_context.RepoMap != null && !string.IsNullOrEmpty(_context.Metrics.CommitHash))
        {
            _context.RepoMap.LastCommitHash = _context.Metrics.CommitHash;
            _context.RepoMap.LastScannedUtc = DateTime.UtcNow;
            await _context.MemoryService.SaveRepoMapAsync(_context.RepoRoot, _context.RepoMap, cancellationToken);
        }

        _context.Logger.LogEvent("Orchestrator", "RunCompleted", "Misión completada con éxito. Guardando logs y reporte.");
        _context.Logger.SaveJson();
        RunReportGenerator.GenerateReport(_context, diffSummary);

        ConsoleReportRenderer.PrintSuccessSummary(_context, diffSummary);
    }

    private async Task AbortAndCleanupAsync(string reason, CancellationToken cancellationToken = default)
    {
        _context.Metrics.Success = false;
        _context.Metrics.ErrorMessage = reason;
        _context.Metrics.Stop();

        _context.Logger.LogEvent("Orchestrator", "RunAborted", $"Misión abortada: {reason}");
        _context.Logger.SaveJson();
        RunReportGenerator.GenerateReport(_context);

        AnsiConsole.MarkupLine($"[bold red]💥 Abortando cambios:[/] {Markup.Escape(reason)}");
        AnsiConsole.MarkupLine("[yellow]Revertiendo cambios en el repositorio...[/]");

        await _gitTools.Revert(cancellationToken);

        bool hasVerifiedCommits = _context.Plan != null && _context.Plan.Milestones.Any(m => !string.IsNullOrEmpty(m.CheckpointCommitHash));

        if (hasVerifiedCommits)
        {
            AnsiConsole.MarkupLine($"[bold yellow]ℹ [[Orchestrator]][/] Se preservó la rama '[cyan]{Markup.Escape(_context.TargetBranch)}[/]' en el último checkpoint verde con los hitos completados previamente.");
        }
        else
        {
            if (!string.IsNullOrEmpty(_context.OriginalBranch))
            {
                await _gitTools.CheckoutBranch(_context.OriginalBranch, cancellationToken);
                AnsiConsole.MarkupLine($"[dim]Restaurada la rama original '{Markup.Escape(_context.OriginalBranch)}'.[/]");
            }

            if (!string.IsNullOrEmpty(_context.TargetBranch) && _context.TargetBranch != _context.OriginalBranch)
            {
                await _gitTools.DeleteBranch(_context.TargetBranch, cancellationToken);
                AnsiConsole.MarkupLine($"[dim]Eliminada rama temporal huérfana '{Markup.Escape(_context.TargetBranch)}'.[/]");
            }
        }

        ConsoleReportRenderer.PrintFailureSummary(_context);
    }
}
