using DevBot.Cli.Core.Auditing;
using DevBot.Cli.Core.Interactive;
using DevBot.Cli.Core.Memory;
using DevBot.Cli.Core.Modes;
using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Subagents;
using DevBot.Cli.Tools;
using Spectre.Console;

namespace DevBot.Cli.Core;

public class Orchestrator
{
    private readonly AgentContext _context;
    private readonly GitTools _gitTools;
    private readonly ScoutAgent _scoutAgent = new();
    private readonly PlannerAgent _plannerAgent = new();
    private readonly CoderAgent _coderAgent = new();
    private readonly ReviewerAgent _reviewerAgent = new();

    public Orchestrator(AgentContext context)
    {
        _context = context;
        _gitTools = new GitTools(context.RepoRoot);
    }

    public async Task<bool> RunAsync(string taskDescription, CancellationToken cancellationToken = default)
    {
        _context.Metrics.Start();

        AnsiConsole.Write(new Rule("[bold cyan]🚀 DevBot - Motor Agéntico de Desarrollo Autónomo[/]").RuleStyle("cyan"));
        AnsiConsole.MarkupLine($"[grey]Directorio de trabajo:[/] [yellow]{_context.RepoRoot}[/]");
        AnsiConsole.MarkupLine($"[grey]Modelo IA:[/] [cyan]{_context.ModelId}[/]");
        AnsiConsole.MarkupLine($"[grey]Modo de operación:[/] [bold magenta]{_context.ModePolicy.DisplayName}[/]");
        AnsiConsole.MarkupLine($"[grey]Requerimiento:[/] [white]{Markup.Escape(taskDescription)}[/]\n");
        _context.Logger.LogEvent("Orchestrator", "ModeConfig", $"Modo: {_context.ModePolicy.DisplayName}");

        // 1. Git Initialization & Feature Branch Setup
        bool isGit = await _gitTools.IsGitRepositoryAsync();
        if (!isGit)
        {
            AnsiConsole.MarkupLine("[bold red]❌ El directorio especificado no es un repositorio Git válido.[/]");
            _context.Metrics.Stop();
            _context.Metrics.ErrorMessage = "Not a git repository.";
            return false;
        }

        _context.OriginalBranch = await _gitTools.GetCurrentBranchAsync();
        string branchSlug = GitTools.CreateSlug(taskDescription);
        string uniqueSuffix = DateTime.UtcNow.ToString("MMdd-HHmm");
        _context.TargetBranch = $"feature/{branchSlug}-{uniqueSuffix}";

        AnsiConsole.MarkupLine($"[grey]Rama original:[/] [dim]{_context.OriginalBranch}[/]");
        AnsiConsole.MarkupLine($"[grey]Creando rama de trabajo:[/] [green]{_context.TargetBranch}[/]");

        string branchResult = await _gitTools.CheckoutNewBranch(_context.TargetBranch);
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(branchResult)}[/]\n");

        // 2. Initialize .agent/, exclude it in .git/info/exclude, and save task.md
        _gitTools.EnsureGitExclude(".agent/");
        _context.SaveTask(taskDescription);
        AnsiConsole.MarkupLine($"[bold blue]📁 [[Orchestrator]][/] Requerimiento guardado en [yellow]{Markup.Escape(_context.TaskFilePath)}[/]");

        // 3. Multilingual Stack Detection
        var stackDetector = new ProjectStackDetector();
        _context.StackInfo = await stackDetector.DetectAsync(_context.RepoRoot, cancellationToken);
        _context.Strategy = StrategyResolver.Resolve(_context.StackInfo);

        AnsiConsole.MarkupLine($"[grey]Stack detectado:[/] [bold green]{_context.StackInfo.StackType}[/] ([cyan]{_context.StackInfo.Language}[/] vía [yellow]{_context.StackInfo.BuildTool}[/])");
        _context.Logger.LogEvent("Orchestrator", "StackDetection", $"Stack: {_context.StackInfo.StackType}, Language: {_context.StackInfo.Language}, Strategy: {_context.Strategy.Name}");

        // 4. Memory and Local Team Rules (.devbot/ & .devbotrules)
        _context.LocalRules = await _context.MemoryService.LoadLocalRulesAsync(_context.RepoRoot, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_context.LocalRules))
        {
            AnsiConsole.MarkupLine("[bold cyan]📜 [[Orchestrator]][/] Reglas locales del equipo detectadas (.devbotrules). Inyectando directrices.");
            _context.Logger.LogEvent("Orchestrator", "LocalRulesLoaded", "Reglas locales .devbotrules cargadas con éxito.");
        }

        string originalCommitHash = await _gitTools.GetLastCommitHashAsync();
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

        // 5. Invoke Scout Subagent
        AnsiConsole.Write(new Rule("[bold cyan]Fase 1: Exploración y Análisis de Arquitectura[/]").RuleStyle("cyan"));
        _context.Logger.LogEvent("Orchestrator", "PhaseStart", "Iniciando Fase 1: Scout Agent");
        _context.Metrics.StartPhase("Scout Agent");
        bool scoutOk = await _scoutAgent.ExecuteAsync(_context, cancellationToken);
        _context.Metrics.EndPhase(scoutOk, scoutOk ? "Exploración completada" : "Fallo en exploración");
        _context.Logger.LogEvent("Orchestrator", "PhaseEnd", $"Fase 1 finalizada: {(scoutOk ? "OK" : "FAILED")}");
        if (!scoutOk)
        {
            AnsiConsole.MarkupLine("[bold red]❌ [[Orchestrator]][/] La fase Scout falló. Abortando misión.");
            await AbortAndCleanupAsync("Fallo durante la fase Scout");
            return false;
        }

        // 5. Human-in-the-Loop Confirmation Gate (unless AutoApprove is set)
        if (!_context.AutoApprove)
        {
            string scoutReport = _context.ReadScoutReport();
            var planSummary = ScoutPlanExtractor.ExtractSummary(
                _context.TaskDescription,
                _context.Mode,
                _context.StackInfo,
                _context.Strategy,
                scoutReport
            );

            var decision = await _context.FeedbackHandler.RequestScoutApprovalAsync(planSummary, cancellationToken);
            if (decision.Type == HumanDecisionType.Aborted)
            {
                await AbortAndCleanupAsync("Misión abortada por el desarrollador tras revisar la fase Scout.");
                return false;
            }

            if (decision.Type == HumanDecisionType.Clarified && !string.IsNullOrWhiteSpace(decision.AdditionalGuidance))
            {
                _context.AdditionalUserGuidance = decision.AdditionalGuidance;
                _context.Logger.LogEvent("Orchestrator", "HumanFeedback", $"Aclaración del usuario: {decision.AdditionalGuidance}");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[dim grey]⚡ [[Orchestrator]][/] Flag --yes activo: Omitiendo confirmación interactiva.[/]");
        }

        // 6. Invoke Planner Subagent (Hierarchical Task Decomposition)
        AnsiConsole.Write(new Rule("[bold cyan]Fase 2: Planificación y Descomposición en Hitos[/]").RuleStyle("cyan"));
        _context.Logger.LogEvent("Orchestrator", "PhaseStart", "Iniciando Fase 2: Planner Agent");
        _context.Metrics.StartPhase("Planner Agent");
        bool plannerOk = await _plannerAgent.ExecuteAsync(_context, cancellationToken);
        _context.Metrics.EndPhase(plannerOk, plannerOk ? "Plan generado" : "Fallo en planificación");
        _context.Logger.LogEvent("Orchestrator", "PhaseEnd", $"Fase 2 finalizada: {(plannerOk ? "OK" : "FAILED")}");
        if (!plannerOk || _context.Plan == null || _context.Plan.Milestones.Count == 0)
        {
            AnsiConsole.MarkupLine("[bold red]❌ [[Orchestrator]][/] La fase Planner falló. Abortando misión.");
            await AbortAndCleanupAsync("Fallo durante la fase de planificación.");
            return false;
        }

        // 7. Sequential Milestone Execution Loop with Git Checkpoints
        AnsiConsole.Write(new Rule("[bold yellow]Fase 3: Bucle Quirúrgico de Hitos y Checkpoints[/]").RuleStyle("yellow"));
        string lastCheckpointCommitHash = await _gitTools.GetLastCommitHashAsync();
        int totalMilestones = _context.Plan.Milestones.Count;

        for (int mIdx = 0; mIdx < totalMilestones; mIdx++)
        {
            var milestone = _context.Plan.Milestones[mIdx];
            _context.CurrentMilestone = milestone;

            AnsiConsole.MarkupLine($"\n[bold white on blue] 📍 HITO {milestone.Order} DE {totalMilestones}: {Markup.Escape(milestone.Title)} [/]");
            AnsiConsole.MarkupLine($"[grey]Capa:[/] [magenta]{Markup.Escape(milestone.Scope)}[/] | [grey]Verificación:[/] [green]{Markup.Escape(milestone.VerificationCommand)}[/]");
            if (milestone.TargetFiles.Count > 0)
            {
                AnsiConsole.MarkupLine($"[grey]Archivos objetivo:[/] {string.Join(", ", milestone.TargetFiles.Select(f => $"[yellow]{Markup.Escape(f)}[/]"))}");
            }
            AnsiConsole.MarkupLine($"[grey]Alcance:[/] [cyan]{Markup.Escape(milestone.Description)}[/]\n");

            bool milestoneTestsPassed = false;

            for (int attempt = 1; attempt <= _context.MaxRetries; attempt++)
            {
                AnsiConsole.MarkupLine($"[dim]─── Hito #{milestone.Order} | Intento #{attempt} / {_context.MaxRetries} ───[/]");

                // Run Coder Agent
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

                // Run Reviewer Agent
                string reviewerPhase = $"Reviewer (Hito {milestone.Order}, Iter #{attempt})";
                _context.Logger.LogEvent("Orchestrator", "PhaseStart", $"Iniciando {reviewerPhase}");
                _context.Metrics.StartPhase(reviewerPhase);
                bool reviewerPassed = await _reviewerAgent.ExecuteAsync(_context, cancellationToken);
                _context.Metrics.EndPhase(reviewerPassed);
                _context.Logger.LogEvent("Orchestrator", "PhaseEnd", $"{reviewerPhase} finalizado: {(reviewerPassed ? "ALL_PASS" : "FAILURES_DETECTED")}");

                if (reviewerPassed)
                {
                    milestoneTestsPassed = true;
                    AnsiConsole.MarkupLine($"[bold green]🎉 [[Orchestrator]][/] ¡Hito #{milestone.Order} verificado exitosamente en el intento #{attempt}!");
                    break;
                }

                if (attempt < _context.MaxRetries)
                {
                    AnsiConsole.MarkupLine($"[yellow]⚠ Las pruebas del hito no pasaron. Retroalimentando resultados a CoderAgent para reintento #{attempt + 1}...[/]");
                }
            }

            if (!milestoneTestsPassed)
            {
                AnsiConsole.MarkupLine($"[bold red]❌ [[Orchestrator]][/] El Hito #{milestone.Order} ('{Markup.Escape(milestone.Title)}') falló tras {_context.MaxRetries} intentos.");
                AnsiConsole.MarkupLine($"[yellow]🔄 Realizando rollback al último checkpoint verde ({lastCheckpointCommitHash})...[/]");
                await _gitTools.ResetHardToCheckpointAsync(lastCheckpointCommitHash);
                await AbortAndCleanupAsync($"Fallo en el Hito #{milestone.Order} tras {_context.MaxRetries} intentos.");
                return false;
            }

            // Pre-Commit Security & Quality Audit Gate on current changes
            if (_context.ModifiedFiles.Count > 0)
            {
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
                    AnsiConsole.MarkupLine($"[yellow]🔄 Realizando rollback al último checkpoint verde ({lastCheckpointCommitHash})...[/]");
                    await _gitTools.ResetHardToCheckpointAsync(lastCheckpointCommitHash);
                    await AbortAndCleanupAsync(auditResult.Summary);
                    return false;
                }

                AnsiConsole.MarkupLine($"[bold green]✔ [[Auditor]][/] Auditoría de seguridad aprobada para Hito #{milestone.Order}.");
            }

            // Checkpoint Commit for this milestone
            string commitPrefix = _context.Mode switch
            {
                AgentMode.Bug => "fix",
                AgentMode.Refactor => "refactor",
                AgentMode.Test => "test",
                _ => "feat"
            };
            string scopeStr = !string.IsNullOrWhiteSpace(milestone.Scope) ? $"({milestone.Scope})" : string.Empty;
            string checkpointMsg = $"{commitPrefix}{scopeStr}: {milestone.Title}";

            string commitResult = await _gitTools.Commit(checkpointMsg);
            if (commitResult.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[bold red]❌ Error creando commit de checkpoint:[/] {Markup.Escape(commitResult)}");
                await _gitTools.ResetHardToCheckpointAsync(lastCheckpointCommitHash);
                await AbortAndCleanupAsync(commitResult);
                return false;
            }

            milestone.IsCompleted = true;
            lastCheckpointCommitHash = await _gitTools.GetLastCommitHashAsync();
            milestone.CheckpointCommitHash = lastCheckpointCommitHash;
            AnsiConsole.MarkupLine($"[bold green]💾 Checkpoint guardado:[/] [cyan]{lastCheckpointCommitHash}[/] [dim]({checkpointMsg})[/]");
            _context.SavePlan(_context.Plan);
        }

        // 8. Remote Push and Pull Request Creation
        AnsiConsole.Write(new Rule("[bold green]Fase 4: Publicación Remota y Apertura de Pull Request[/]").RuleStyle("green"));

        // 1. Remote push
        AnsiConsole.MarkupLine($"[cyan]🚀 Publicando rama remota en origin/{Markup.Escape(_context.TargetBranch)}...[/]");
        var pushResult = await _gitTools.PushBranch(_context.TargetBranch);
        if (pushResult.Success)
        {
            AnsiConsole.MarkupLine($"[bold green]✔ Rama remota publicada exitosamente: origin/{Markup.Escape(_context.TargetBranch)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ No se pudo hacer push a origin:[/] [dim]{Markup.Escape(pushResult.Error)}[/]");
        }

        // 2. Pull Request creation (waiting strictly for human manual approval, no automerge!)
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
        var prResult = await _gitTools.CreatePullRequestAsync(_context.TargetBranch, prBaseBranch, prTitle, prBody);
        if (prResult.Success)
        {
            _context.PullRequestUrl = prResult.PrUrl;
            AnsiConsole.MarkupLine($"[bold green]✔ {Markup.Escape(prResult.Message)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ {Markup.Escape(prResult.Message)}[/]");
        }

            string diffSummary = await _gitTools.GetDiffSummaryAsync();
            _context.Metrics.CommitHash = await _gitTools.GetLastCommitHashAsync();
            _context.Metrics.Success = true;
            _context.Metrics.Stop();

            // Update persistent RepoMap with latest commit hash
            if (_context.RepoMap != null && !string.IsNullOrEmpty(_context.Metrics.CommitHash))
            {
                _context.RepoMap.LastCommitHash = _context.Metrics.CommitHash;
                _context.RepoMap.LastScannedUtc = DateTime.UtcNow;
                await _context.MemoryService.SaveRepoMapAsync(_context.RepoRoot, _context.RepoMap, cancellationToken);
            }

            // Export structured logs and executive markdown report
            _context.Logger.LogEvent("Orchestrator", "RunCompleted", "Misión completada con éxito. Guardando logs y reporte.");
            _context.Logger.SaveJson();
            RunReportGenerator.GenerateReport(_context, diffSummary);

            PrintSuccessSummary(diffSummary);
        return true;
    }

    private async Task AbortAndCleanupAsync(string reason)
    {
        _context.Metrics.Success = false;
        _context.Metrics.ErrorMessage = reason;
        _context.Metrics.Stop();

        _context.Logger.LogEvent("Orchestrator", "RunAborted", $"Misión abortada: {reason}");
        _context.Logger.SaveJson();
        RunReportGenerator.GenerateReport(_context);

        AnsiConsole.MarkupLine($"[bold red]💥 Abortando cambios:[/] {Markup.Escape(reason)}");
        AnsiConsole.MarkupLine("[yellow]Revertiendo cambios en el repositorio...[/]");

        await _gitTools.Revert();

        if (!string.IsNullOrEmpty(_context.OriginalBranch))
        {
            await _gitTools.CheckoutBranch(_context.OriginalBranch);
            AnsiConsole.MarkupLine($"[dim]Restaurada la rama original '{Markup.Escape(_context.OriginalBranch)}'.[/]");
        }

        PrintFailureSummary();
    }

    private void PrintSuccessSummary(string diffSummary)
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow("[bold green]Estado:[/]", "[green]COMPLETADO CON ÉXITO[/]");
        grid.AddRow("[bold cyan]Rama Local:[/]", $"[cyan]{Markup.Escape(_context.TargetBranch)}[/]");
        grid.AddRow("[bold cyan]Rama Remota Publicada:[/]", $"[bold cyan]origin/{Markup.Escape(_context.TargetBranch)}[/]");
        grid.AddRow("[bold yellow]Commit Hash:[/]", $"[yellow]{Markup.Escape(_context.Metrics.CommitHash ?? "")}[/]");
        if (!string.IsNullOrEmpty(_context.PullRequestUrl))
        {
            grid.AddRow("[bold green]Pull Request:[/]", $"[link={_context.PullRequestUrl}][bold underline green]{Markup.Escape(_context.PullRequestUrl)}[/][/]");
        }
        grid.AddRow("[bold]Tiempo Transcurrido:[/]", $"{_context.Metrics.Elapsed.TotalSeconds:F1}s");
        grid.AddRow("[bold]Reintentos Coder/Reviewer:[/]", $"{_context.Metrics.CoderRetries}");
        grid.AddRow("[bold]Tokens Consumidos (estimados):[/]", $"{_context.Metrics.TokensConsumed:N0}");

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("[bold green] ✔ RESUMEN DE PULL REQUEST [/]"),
            Border = BoxBorder.Double,
            Padding = new Padding(1, 1, 1, 1)
        };

        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);

        // Desglose de Telemetría por Fase
        if (_context.Metrics.Phases.Count > 0)
        {
            var phaseTable = new Table().Border(TableBorder.Rounded);
            phaseTable.AddColumn(new TableColumn("[bold cyan]Fase / Subagente[/]"));
            phaseTable.AddColumn(new TableColumn("[bold]Duración[/]").Centered());
            phaseTable.AddColumn(new TableColumn("[bold]Tokens[/]").Centered());
            phaseTable.AddColumn(new TableColumn("[bold]Herramientas Invocadas[/]"));
            phaseTable.AddColumn(new TableColumn("[bold]Estado[/]").Centered());

            foreach (var p in _context.Metrics.Phases)
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

        if (!string.IsNullOrEmpty(_context.PullRequestUrl))
        {
            AnsiConsole.MarkupLine($"\n[bold yellow]👉 Pull Request listo para auditoría humana:[/] [link={_context.PullRequestUrl}][bold underline green]{Markup.Escape(_context.PullRequestUrl)}[/][/]");
            AnsiConsole.MarkupLine("[dim]   (Por política de seguridad, el Pull Request requiere revisión y aprobación manual. Sin automerge.)[/]\n");
        }

        AnsiConsole.MarkupLine($"[bold cyan]📄 Informe Ejecutivo Markdown:[/] [yellow]{Markup.Escape(_context.LatestReportPath)}[/]");
        AnsiConsole.MarkupLine($"[bold grey]📜 Registro de Auditoría (Log):[/] [dim]{Markup.Escape(_context.Logger.LatestLogPath)}[/]\n");

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

    private void PrintFailureSummary()
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow("[bold red]Estado:[/]", "[red]FALLIDO / REVERTIDO[/]");
        grid.AddRow("[bold]Motivo:[/]", $"[red]{Markup.Escape(_context.Metrics.ErrorMessage ?? "")}[/]");
        grid.AddRow("[bold]Tiempo Transcurrido:[/]", $"{_context.Metrics.Elapsed.TotalSeconds:F1}s");
        grid.AddRow("[bold]Reintentos Realizados:[/]", $"{_context.Metrics.CoderRetries}");
        grid.AddRow("[bold]Tokens Consumidos:[/]", $"{_context.Metrics.TokensConsumed:N0}");

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("[bold red] ❌ RESUMEN DE EJECUCIÓN (ROLLBACK) [/]"),
            Border = BoxBorder.Heavy,
            Padding = new Padding(1, 1, 1, 1)
        };

        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);

        if (_context.Metrics.Phases.Count > 0)
        {
            var phaseTable = new Table().Border(TableBorder.Rounded);
            phaseTable.AddColumn(new TableColumn("[bold cyan]Fase / Subagente[/]"));
            phaseTable.AddColumn(new TableColumn("[bold]Duración[/]").Centered());
            phaseTable.AddColumn(new TableColumn("[bold]Tokens[/]").Centered());
            phaseTable.AddColumn(new TableColumn("[bold]Herramientas Invocadas[/]"));
            phaseTable.AddColumn(new TableColumn("[bold]Estado[/]").Centered());

            foreach (var p in _context.Metrics.Phases)
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

        AnsiConsole.MarkupLine($"\n[bold cyan]📄 Informe Ejecutivo del Fallo:[/] [yellow]{Markup.Escape(_context.LatestReportPath)}[/]");
        AnsiConsole.MarkupLine($"[bold grey]📜 Registro de Auditoría (Log):[/] [dim]{Markup.Escape(_context.Logger.LatestLogPath)}[/]\n");
    }
}
