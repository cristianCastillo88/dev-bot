using DevBot.Cli.Tools;

namespace DevBot.Cli.Core.Strategies;

public interface IBuildAndTestStrategy
{
    ProjectStackType SupportedStack { get; }
    string Name { get; }

    Task<StrategyExecutionResult> BuildAsync(TerminalTools terminal, CancellationToken ct = default);
    Task<StrategyExecutionResult> TestAsync(TerminalTools terminal, CancellationToken ct = default);
    Task<StrategyExecutionResult> LintOrFormatAsync(TerminalTools terminal, CancellationToken ct = default);

    string GetIdiomaticPromptGuidelines();
    string GetVerificationInstructions();
}
