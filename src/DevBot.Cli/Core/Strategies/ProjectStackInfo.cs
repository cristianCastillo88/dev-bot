namespace DevBot.Cli.Core.Strategies;

public enum ProjectStackType
{
    DotNet,
    NodeJs,
    JavaMaven,
    Python,
    Go,
    Unknown
}

public record ProjectStackInfo(
    ProjectStackType StackType,
    string Language,
    string BuildTool,
    string? DetectedFile,
    IReadOnlyList<string> SourceDirectories,
    IReadOnlyList<string> TestDirectories
);

public record StrategyExecutionResult(
    bool Success,
    int ExitCode,
    string Output,
    string Errors,
    string CommandExecuted
);
