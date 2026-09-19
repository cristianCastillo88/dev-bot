using DevBot.Cli.Tools;

namespace DevBot.Cli.Core.Strategies;

public class DotNetStrategy : IBuildAndTestStrategy
{
    private readonly string? _targetFile;

    public DotNetStrategy(string? targetFile = null)
    {
        _targetFile = targetFile;
    }

    public ProjectStackType SupportedStack => ProjectStackType.DotNet;
    public string Name => "C# / .NET Strategy";

    public async Task<StrategyExecutionResult> BuildAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string args = string.IsNullOrWhiteSpace(_targetFile) ? "build" : $"build \"{_targetFile}\"";
        string raw = await terminal.RunCommand("dotnet", args);
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, $"dotnet {args}");
    }

    public async Task<StrategyExecutionResult> TestAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string args = string.IsNullOrWhiteSpace(_targetFile) ? "test" : $"test \"{_targetFile}\"";
        string raw = await terminal.RunCommand("dotnet", args);
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, $"dotnet {args}");
    }

    public async Task<StrategyExecutionResult> LintOrFormatAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string args = string.IsNullOrWhiteSpace(_targetFile)
            ? "format --verify-no-changes"
            : $"format \"{_targetFile}\" --verify-no-changes";

        string raw = await terminal.RunCommand("dotnet", args);
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, $"dotnet {args}");
    }

    public string GetIdiomaticPromptGuidelines()
    {
        return """
            [C# / .NET IDIOMATIC CONVENTIONS]
            - Use modern C# 12 / .NET 8 idioms (file-scoped namespaces, pattern matching, readonly structs/records where appropriate).
            - Respect Nullable Reference Types (`#nullable enable`). Avoid discarding null checks or suppressing warnings with `!`.
            - Asynchronous methods MUST return `Task` or `Task<T>` (never `async void` except UI events) and propagate `CancellationToken`.
            - Follow Dependency Injection patterns and interface segregation; do not instantiate heavy service dependencies directly.
            - Write clean, expressive unit tests using xUnit / NUnit with descriptive test method names and Arrange-Act-Assert structure.
            """;
    }

    public string GetVerificationInstructions()
    {
        return """
            1. Execute the .NET test suite using `RunCommand` in TerminalTools with command 'dotnet test' (or command 'dotnet' and arguments 'test').
            2. If there are compilation or build errors, run 'dotnet build' to inspect detailed diagnostic messages.
            """;
    }

    private static int ParseExitCode(string rawOutput)
    {
        if (rawOutput.StartsWith("Exit Code:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = rawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && int.TryParse(parts[0].Replace("Exit Code:", "").Trim(), out int code))
            {
                return code;
            }
        }
        return rawOutput.Contains("Exit Code: 0") ? 0 : 1;
    }
}
