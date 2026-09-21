using System.Text.RegularExpressions;
using DevBot.Cli.Core;
using DevBot.Cli.Core.Planning;
using DevBot.Cli.Prompts;
using DevBot.Cli.Tools;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console;

namespace DevBot.Cli.Subagents;

public class PlannerAgent : SubagentBase, IPlannerAgent
{
    public override string Name => "Planner Agent";

    public override async Task<bool> ExecuteAsync(AgentContext context, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[bold cyan]📋 [[Planner Agent]][/] Descomponiendo el requerimiento en hitos atómicos...");

        // 1. Load system prompt
        string systemPrompt = PromptLoader.LoadPrompt("PlannerPrompt.txt", "You are the PLANNER AGENT. Decompose the task into verifiable milestones.");

        // 2. Read-only plugin (for querying files if needed)
        var scoutPlugin = FileTools.CreateScoutPlugin(context.RepoRoot, context);
        var kernel = CreateSubagentKernel(context, new[] { scoutPlugin });

        // 3. Prepare clean ChatHistory
        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);

        string task = context.ReadTask();
        string scoutReport = context.ReadScoutReport();
        string defaultVerifyCmd = context.StackInfo.BuildTool switch
        {
            "dotnet" => !string.IsNullOrWhiteSpace(context.StackInfo.DetectedFile)
                ? $"dotnet test \"{context.StackInfo.DetectedFile}\""
                : "dotnet test",
            "npm" => "npm test",
            "mvn" => "mvn test",
            "gradle" => "gradle test",
            "pytest" or "python" => "pytest",
            _ => !string.IsNullOrWhiteSpace(context.StackInfo.DetectedFile)
                ? $"dotnet test \"{context.StackInfo.DetectedFile}\""
                : "dotnet test"
        };

        string detectedFileBlock = !string.IsNullOrWhiteSpace(context.StackInfo.DetectedFile)
            ? $"\nPRIMARY BUILD/SOLUTION FILE: {context.StackInfo.DetectedFile}\n(MANDATORY: When generating VerificationCommand for milestones, ALWAYS target this exact file: `dotnet test \"{context.StackInfo.DetectedFile}\"`)\n"
            : string.Empty;

        string localRulesBlock = !string.IsNullOrWhiteSpace(context.LocalRules)
            ? $"\nLOCAL REPOSITORY RULES (.devbotrules):\n{context.LocalRules}\n"
            : string.Empty;

        string guidanceBlock = !string.IsNullOrWhiteSpace(context.AdditionalUserGuidance)
            ? $"\nADDITIONAL DEVELOPER GUIDANCE (HITL):\n{context.AdditionalUserGuidance}\n"
            : string.Empty;

        string userPrompt = $"""
            TASK REQUIREMENT:
            {task}
            {guidanceBlock}
            OPERATIONAL MODE:
            {context.ModePolicy.DisplayName}

            PROJECT STACK:
            Language: {context.StackInfo.Language} ({context.StackInfo.StackType})
            Build Tool: {context.StackInfo.BuildTool}
            Default Verification Command: {defaultVerifyCmd}
            {detectedFileBlock}
            {localRulesBlock}
            SCOUT TECHNICAL REPORT:
            {scoutReport}

            INSTRUCTIONS:
            1. Analyze the Scout report and determine if this task requires 1 atomic milestone or N sequential bottom-up milestones.
            2. Decompose into atomic milestones where each touches at most 2 to 3 files and specifies an exact verification command.
            3. Return your plan enclosed in a ```json ``` block conforming to the requested schema.
            """;

        history.AddUserMessage(userPrompt);

        // 4. Run execution loop
        string result = await RunChatLoopAsync(kernel, history, context, cancellationToken, "[cyan]📋 Planner estructurando hitos de desarrollo...[/]");

        // 5. Extract JSON and deserialize PlanDefinition
        var plan = ExtractPlanDefinition(result, task, defaultVerifyCmd);

        // 6. Save plan to context & files
        context.SavePlan(plan);

        // 7. Render plan in console
        RenderPlanTable(plan);

        return true;
    }

    private static PlanDefinition ExtractPlanDefinition(string responseText, string originalTask, string defaultVerifyCmd)
    {
        PlanDefinition? plan = null;

        // Try extracting from ```json ... ``` block
        var match = Regex.Match(responseText, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            plan = PlanDefinition.FromJson(match.Groups[1].Value);
        }

        // Fallback: search for first { and last }
        if (plan == null)
        {
            int firstBrace = responseText.IndexOf('{');
            int lastBrace = responseText.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                string candidate = responseText.Substring(firstBrace, lastBrace - firstBrace + 1);
                plan = PlanDefinition.FromJson(candidate);
            }
        }

        // Fallback if parsing failed or no milestones produced: generate single default milestone
        if (plan == null || plan.Milestones.Count == 0)
        {
            plan = new PlanDefinition
            {
                TaskDescription = originalTask,
                ArchitecturalSummary = "Plan atómico generado por fallback para ejecución directa.",
                Milestones = new List<PlanMilestone>
                {
                    new()
                    {
                        Order = 1,
                        Id = "m1",
                        Title = "Implementación del requerimiento",
                        Scope = "core",
                        Description = originalTask,
                        TargetFiles = new List<string>(),
                        VerificationCommand = defaultVerifyCmd,
                        RequiredSkills = new List<string> { "fullstack" }
                    }
                }
            };
        }

        // Normalize milestones
        for (int i = 0; i < plan.Milestones.Count; i++)
        {
            var m = plan.Milestones[i];
            m.Order = i + 1;
            if (string.IsNullOrWhiteSpace(m.Id)) m.Id = $"m{m.Order}";
            if (string.IsNullOrWhiteSpace(m.VerificationCommand)) m.VerificationCommand = defaultVerifyCmd;
            if (string.IsNullOrWhiteSpace(m.Scope)) m.Scope = "core";
        }

        return plan;
    }

    private static void RenderPlanTable(PlanDefinition plan)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.Title($"[bold cyan]Plan de Implementación ({plan.Milestones.Count} {(plan.Milestones.Count == 1 ? "Hito" : "Hitos")})[/]");
        table.AddColumn(new TableColumn("[bold grey]#[/]"));
        table.AddColumn(new TableColumn("[bold yellow]Hito / Título[/]"));
        table.AddColumn(new TableColumn("[bold magenta]Capa (Scope)[/]"));
        table.AddColumn(new TableColumn("[bold blue]Archivos Objetivo[/]"));
        table.AddColumn(new TableColumn("[bold green]Comando de Verificación[/]"));

        foreach (var m in plan.Milestones)
        {
            string filesStr = m.TargetFiles.Count > 0
                ? string.Join("\n", m.TargetFiles.Select(f => $"• [dim]{Markup.Escape(f)}[/]"))
                : "[dim grey](por definir)[/]";

            table.AddRow(
                m.Order.ToString(),
                $"[bold]{Markup.Escape(m.Title)}[/]\n[dim]{Markup.Escape(m.Description.Length > 80 ? m.Description[..77] + "..." : m.Description)}[/]",
                $"[magenta]{Markup.Escape(m.Scope)}[/]",
                filesStr,
                $"[green]{Markup.Escape(m.VerificationCommand)}[/]"
            );
        }

        AnsiConsole.Write(table);
    }
}
