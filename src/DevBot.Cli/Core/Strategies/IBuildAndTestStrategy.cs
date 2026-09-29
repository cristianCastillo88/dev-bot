using DevBot.Cli.Tools;

namespace DevBot.Cli.Core.Strategies;

public interface IBuildAndTestStrategy
{
    ProjectStackType SupportedStack { get; }
    string Name { get; }

    Task<StrategyExecutionResult> BuildAsync(TerminalTools terminal, CancellationToken ct = default);
    Task<StrategyExecutionResult> TestAsync(TerminalTools terminal, CancellationToken ct = default);
    Task<StrategyExecutionResult> LintOrFormatAsync(TerminalTools terminal, CancellationToken ct = default);

    Task<SyntaxValidationResult> ValidateSyntaxAsync(
        string filePath,
        string content,
        TerminalTools terminal,
        CancellationToken ct = default);

    string GetIdiomaticPromptGuidelines();
    string GetVerificationInstructions();
}

public record SyntaxValidationResult(bool IsValid, string? ErrorMessage)
{
    public static SyntaxValidationResult Success() => new(true, null);
    public static SyntaxValidationResult Failure(string message) => new(false, message);
}
