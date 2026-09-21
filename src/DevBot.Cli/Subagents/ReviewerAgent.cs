using DevBot.Cli.Core;
using DevBot.Cli.Prompts;
using DevBot.Cli.Tools;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Spectre.Console;

namespace DevBot.Cli.Subagents;

public class ReviewerAgent : SubagentBase, IReviewerAgent
{
    public const string AllPassToken = "ALL_PASS";
    public override string Name => "ReviewerAgent (QA / Verificador Determinista)";

    public override async Task<bool> ExecuteAsync(AgentContext context, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[bold magenta]🧪 [[Reviewer Agent]][/] Iniciando verificación determinista y ejecución de pruebas...");

        // 1. Load system prompt
        string systemPrompt = PromptLoader.LoadPrompt("ReviewerPrompt.txt", "You are the REVIEWER AGENT. Run tests and report ALL_PASS or extracted failures.");

        // 2. Strict plugins: TerminalTools (RunCommand) and ReadOnly FileTools (ReadFile)
        var terminalTools = new TerminalTools(context.RepoRoot, context);
        var terminalPlugin = KernelPluginFactory.CreateFromObject(terminalTools, "TerminalTools");
        var reviewerFilePlugin = FileTools.CreateReviewerPlugin(context.RepoRoot, context);

        var kernel = CreateSubagentKernel(context, new[] { terminalPlugin, reviewerFilePlugin });

        // 3. Prepare clean ChatHistory
        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);

        string task = context.ReadTask();
        string milestoneVerification = context.CurrentMilestone != null
            ? $"""

            ACTIVE MILESTONE #{context.CurrentMilestone.Order}: {context.CurrentMilestone.Title}
            SPECIFIC MILESTONE VERIFICATION COMMAND:
            `{context.CurrentMilestone.VerificationCommand}`
            (Run this command with RunCommand to verify the milestone).

            """
            : string.Empty;

        string userPrompt = $"""
            VERIFICATION TASK:
            {task}
            {milestoneVerification}
            DETECTED STACK:
            Language/Framework: {context.StackInfo.Language} ({context.StackInfo.StackType})
            Strategy: {context.Strategy.Name}
            Target Solution/Project: {context.StackInfo.DetectedFile ?? "Repository Root"}

            OPERATIONAL MODE:
            {context.ModePolicy.DisplayName}

            {context.ModePolicy.ReviewerInstructions}

            VERIFICATION INSTRUCTIONS FOR THIS STACK:
            {context.Strategy.GetVerificationInstructions()}

            RULES:
            1. If all builds and tests pass cleanly with exit code 0:
               - Include the exact verdict line: `VERDICT: ALL_PASS`
               - Summarize tests executed.
            2. If any build or test fails:
               - Include the exact verdict line: `VERDICT: FAILED`
               - Do NOT say `ALL_PASS`.
               - Extract ONLY the relevant error messages, failed tests, expected vs actual values, and stack traces.
               - DO NOT include thousands of irrelevant console lines.
            """;

        history.AddUserMessage(userPrompt);

        // 4. Run execution loop
        string reviewResult = await RunChatLoopAsync(kernel, history, context, cancellationToken, $"[magenta]🧪 Reviewer ejecutando suite de pruebas con {context.ModelId}...[/]");

        bool verdictFailed = reviewResult.Contains("VERDICT: FAILED", StringComparison.OrdinalIgnoreCase) ||
                             reviewResult.Contains("NOT ALL_PASS", StringComparison.OrdinalIgnoreCase) ||
                             reviewResult.Contains("FAILURES_DETECTED", StringComparison.OrdinalIgnoreCase);

        bool hasAllPassToken = reviewResult.Contains("VERDICT: ALL_PASS", StringComparison.OrdinalIgnoreCase) ||
                               (reviewResult.Contains(AllPassToken, StringComparison.OrdinalIgnoreCase) && !verdictFailed);

        // Deterministic safety check: if terminal command exited with non-zero exit code, it cannot be ALL_PASS
        bool commandExitCodeFailed = context.LastCommandExitCode.HasValue && context.LastCommandExitCode.Value != 0;

        bool allPass = hasAllPassToken && !verdictFailed && !commandExitCodeFailed;

        // 5. Persist review results
        context.SaveReviewResults(reviewResult);

        if (allPass)
        {
            AnsiConsole.MarkupLine($"[bold green]✔ [[Reviewer Agent]][/] [green]ALL_PASS[/] - Todas las pruebas y verificaciones pasaron exitosamente.");
            return true;
        }
        else
        {
            string reason = commandExitCodeFailed 
                ? $"Se detectaron fallos en la ejecución de comandos (código de salida: {context.LastCommandExitCode})."
                : "Se detectaron fallos en las pruebas según el análisis de resultados.";
            AnsiConsole.MarkupLine($"[bold red]❌ [[Reviewer Agent]][/] {reason} Detalle guardado en [yellow]{Markup.Escape(context.ReviewResultsPath)}[/].");
            return false;
        }
    }
}
