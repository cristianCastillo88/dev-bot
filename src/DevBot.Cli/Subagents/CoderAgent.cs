using DevBot.Cli.Core;
using DevBot.Cli.Prompts;
using DevBot.Cli.Tools;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console;

namespace DevBot.Cli.Subagents;

public class CoderAgent : SubagentBase, ICoderAgent
{
    public override string Name => "CoderAgent (Implementador Quirúrgico)";

    public override async Task<bool> ExecuteAsync(AgentContext context, CancellationToken cancellationToken = default)
    {
        int iteration = context.Metrics.CoderRetries + 1;
        AnsiConsole.MarkupLine($"[bold yellow]🔨 [[Coder Agent]][/] Iniciando ciclo de implementación (Iteración #{iteration}/{context.MaxRetries})...");

        // 1. Load system prompt
        string systemPrompt = PromptLoader.LoadPrompt("CoderPrompt.txt", "You are the CODER AGENT. Make surgical edits to code.");

        // 2. Strict file modification plugin (ReadFile, WriteFile, ApplyDiff)
        var coderPlugin = FileTools.CreateCoderPlugin(context.RepoRoot, context);
        var kernel = CreateSubagentKernel(context, new[] { coderPlugin });

        // 3. Prepare clean ChatHistory
        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);

        string task = context.ReadTask();
        string idiomaticGuidelines = context.Strategy.GetIdiomaticPromptGuidelines();
        string guidanceBlock = !string.IsNullOrWhiteSpace(context.AdditionalUserGuidance)
            ? $"\nADDITIONAL DEVELOPER CLARIFICATIONS / DIRECTIVES:\n{context.AdditionalUserGuidance}\n"
            : string.Empty;

        string localRulesBlock = !string.IsNullOrWhiteSpace(context.LocalRules)
            ? $"\nLOCAL REPOSITORY RULES & CONSTRAINTS (.devbotrules):\n{context.LocalRules}\n"
            : string.Empty;

        string milestoneBlock = context.CurrentMilestone != null
            ? $"""

            ACTIVE MILESTONE #{context.CurrentMilestone.Order}: {context.CurrentMilestone.Title} (Scope: {context.CurrentMilestone.Scope})
            MILESTONE SPECIFIC GOAL:
            {context.CurrentMilestone.Description}
            TARGET FILES (STRICTLY CONFINED TO THIS MILESTONE):
            {(context.CurrentMilestone.TargetFiles.Count > 0 ? string.Join(", ", context.CurrentMilestone.TargetFiles) : "Confine changes to minimal necessary files.")}
            VERIFICATION COMMAND:
            {context.CurrentMilestone.VerificationCommand}

            """
            : string.Empty;

        string userPrompt;

        if (iteration == 1)
        {
            string scoutReport = context.ReadScoutReport();
            userPrompt = $"""
                ASSIGNED TASK:
                {task}
                {guidanceBlock}{localRulesBlock}{milestoneBlock}
                OPERATIONAL MODE:
                {context.ModePolicy.DisplayName}

                {context.ModePolicy.CoderInstructions}

                LANGUAGE & STACK IDIOMATIC GUIDELINES:
                {idiomaticGuidelines}

                SCOUT REPORT ARCHITECTURAL BLUEPRINT:
                {scoutReport}

                INSTRUCTIONS:
                - Implement the required changes respecting the project architecture and reference style conventions.
                - Use `ApplyDiff` for existing files to ensure minimal surgical edits. Ensure the originalSnippet is unique.
                - Use `WriteFile` only if creating new files or replacing an entire file.
                - When complete, provide a summary of the files modified or created.
                """;
        }
        else
        {
            string reviewResults = context.ReadReviewResults();
            userPrompt = $"""
                ASSIGNED TASK:
                {task}
                {guidanceBlock}{localRulesBlock}{milestoneBlock}
                OPERATIONAL MODE:
                {context.ModePolicy.DisplayName}

                {context.ModePolicy.CoderInstructions}

                LANGUAGE & STACK IDIOMATIC GUIDELINES:
                {idiomaticGuidelines}

                PREVIOUS REVIEW RESULTS (FAILURES TO FIX):
                {reviewResults}

                INSTRUCTIONS:
                - Analyze the specific compilation errors or test failures above.
                - Apply surgical fixes using `ApplyDiff` or `WriteFile` to resolve each failure.
                - When finished, summarize the fixes applied.
                """;
        }

        history.AddUserMessage(userPrompt);

        int initialModifiedCount = context.ModifiedFiles.Count;

        // 4. Run execution loop
        string result = await RunChatLoopAsync(kernel, history, context, cancellationToken, $"[yellow]🔨 Coder aplicando modificaciones con {context.ModelId} (Iteración #{iteration})...[/]");

        context.Metrics.CoderRetries++;

        bool anyNewModifications = context.ModifiedFiles.Count > initialModifiedCount;
        if (!anyNewModifications)
        {
            AnsiConsole.MarkupLine($"[bold yellow]⚠ [[Coder Agent]][/] No se registraron nuevos archivos modificados en la iteración #{iteration}.");
            context.Logger.LogEvent("CoderAgent", "Warning", $"Iteración #{iteration}: No se modificaron archivos en el repositorio.");
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold green]✔ [[Coder Agent]][/] Cambios aplicados para iteración #{iteration} (Total archivos modificados: {context.ModifiedFiles.Count}).");
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            var summaryPanel = new Panel(new Text(result.Trim()))
            {
                Header = new PanelHeader($"[bold yellow]Resumen Coder (Iteración #{iteration})[/]"),
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(summaryPanel);
        }

        return true;
    }
}
