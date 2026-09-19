using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core.Auditing;

public record SecurityFinding(
    string FilePath,
    int LineNumber,
    string RuleId,
    string Description,
    string RedactedSnippet
);

public record AuditResult(
    bool Passed,
    IReadOnlyList<SecurityFinding> SecurityFindings,
    StrategyExecutionResult? LinterResult,
    string Summary
);
