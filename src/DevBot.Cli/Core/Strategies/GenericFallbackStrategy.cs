using DevBot.Cli.Tools;

namespace DevBot.Cli.Core.Strategies;

public class GenericFallbackStrategy : IBuildAndTestStrategy
{
    public ProjectStackType SupportedStack => ProjectStackType.Unknown;
    public string Name => "Generic / Unknown Strategy";

    public Task<StrategyExecutionResult> BuildAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        return Task.FromResult(new StrategyExecutionResult(true, 0, "No default build tool detected.", string.Empty, "none"));
    }

    public Task<StrategyExecutionResult> TestAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        return Task.FromResult(new StrategyExecutionResult(true, 0, "No default test runner detected.", string.Empty, "none"));
    }

    public Task<StrategyExecutionResult> LintOrFormatAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        return Task.FromResult(new StrategyExecutionResult(true, 0, "No default linter detected.", string.Empty, "none"));
    }

    public string GetIdiomaticPromptGuidelines()
    {
        return """
            [GENERAL CODE INTEGRITY CONVENTIONS]
            - Respect the existing project conventions, indentation style, and framework patterns.
            - Maintain clean, self-documenting code and avoid breaking unrelated modules.
            """;
    }

    public string GetVerificationInstructions()
    {
        return """
            1. Identify any build or test scripts present in the repository (e.g., Makefile, package.json, test runner scripts).
            2. Run the discovered verification command using `RunCommand` in TerminalTools.
            """;
    }
}
