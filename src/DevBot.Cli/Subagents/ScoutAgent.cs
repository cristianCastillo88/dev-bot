using DevBot.Cli.Core;
using DevBot.Cli.Prompts;
using DevBot.Cli.Tools;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console;

namespace DevBot.Cli.Subagents;

public class ScoutAgent : SubagentBase, IScoutAgent
{
    public override string Name => "ScoutAgent (Analista / Solo Lectura)";

    public override async Task<bool> ExecuteAsync(AgentContext context, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[bold cyan]🔍 [[Scout Agent]][/] Iniciando análisis quirúrgico del repositorio...");

        // 1. Load system prompt
        string systemPrompt = PromptLoader.LoadPrompt("ScoutPrompt.txt", "You are the SCOUT AGENT. Analyze the repo and write scout_report.md.");

        // 2. Strict read-only plugin (ReadFile, ListFiles, SearchCode)
        var scoutPlugin = FileTools.CreateScoutPlugin(context.RepoRoot, context);
        var kernel = CreateSubagentKernel(context, new[] { scoutPlugin });

        // 3. Prepare clean ChatHistory
        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);

        string task = context.ReadTask();
        string localRulesBlock = !string.IsNullOrWhiteSpace(context.LocalRules)
            ? $"\nLOCAL REPOSITORY RULES & CONSTRAINTS (.devbotrules):\n{context.LocalRules}\n"
            : string.Empty;

        string repoMapBlock = context.RepoMap != null && context.RepoMap.Components.Count > 0
            ? $"\nCACHED ARCHITECTURE REPO MAP (.devbot/repo_map.json):\n- Entrypoints: {string.Join(", ", context.RepoMap.Entrypoints)}\n- Layers: {string.Join(", ", context.RepoMap.MainLayers)}\n- Key Modules: {string.Join(", ", context.RepoMap.Components.Take(6).Select(c => $"{c.Name} ({c.Layer})"))}\n"
            : string.Empty;

        string userPrompt = $"""
            TASK REQUIREMENT:
            {task}

            OPERATIONAL MODE:
            {context.ModePolicy.DisplayName}

            {context.ModePolicy.ScoutInstructions}
            {localRulesBlock}{repoMapBlock}
            INSTRUCTIONS:
            1. Use ListFiles or SearchCode to locate candidate files without reading entire files blindly.
            2. Use ReadFile with startLine and endLine to inspect specific methods and understand existing patterns.
            3. Identify an existing reference file that illustrates project conventions.
            4. Produce your final Scout Technical Report in Markdown format with:
               - Problem & Context Analysis
               - Exact Target Files & Locations
               - Reference File & Coding Conventions
               - Step-by-Step Implementation Plan for Coder Agent
               - Verification Commands (e.g., dotnet test, npm test)
            """;

        history.AddUserMessage(userPrompt);

        // 4. Run execution loop
        string report = await RunChatLoopAsync(kernel, history, context, cancellationToken, $"[cyan]🔍 Scout analizando el código del repositorio con {context.ModelId}...[/]");

        if (string.IsNullOrWhiteSpace(report))
        {
            AnsiConsole.MarkupLine("[bold red]❌ [[Scout Agent]][/] No se pudo generar el reporte de exploración.");
            return false;
        }

        // 5. Persist deliverable to .agent/scout_report.md
        context.SaveScoutReport(report);
        AnsiConsole.MarkupLine($"[bold green]✔ [[Scout Agent]][/] Reporte generado y persistido en [yellow]{Markup.Escape(context.ScoutReportPath)}[/]");
        return true;
    }
}
