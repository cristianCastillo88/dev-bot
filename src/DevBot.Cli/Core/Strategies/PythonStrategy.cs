using DevBot.Cli.Tools;

namespace DevBot.Cli.Core.Strategies;

public class PythonStrategy : IBuildAndTestStrategy
{
    public ProjectStackType SupportedStack => ProjectStackType.Python;
    public string Name => "Python Strategy";

    public Task<StrategyExecutionResult> BuildAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        // Python is an interpreted language; build is a no-op success
        return Task.FromResult(new StrategyExecutionResult(true, 0, "No compilation required for Python.", string.Empty, "none"));
    }

    public async Task<StrategyExecutionResult> TestAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string raw = await terminal.RunCommand("pytest", "-v");
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, "pytest -v");
    }

    public async Task<StrategyExecutionResult> LintOrFormatAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string raw = await terminal.RunCommand("flake8", ".");
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, "flake8 .");
    }

    public string GetIdiomaticPromptGuidelines()
    {
        return """
            [PYTHON IDIOMATIC CONVENTIONS]
            - Follow PEP 8 style guides, PEP 484 type hints, and PEP 257 docstring conventions.
            - Write clean, modular Python 3.10+ code with dataclasses / Pydantic models when appropriate.
            - Handle exceptions cleanly with specific exception types; avoid bare `except:`.
            - Write tests with `pytest` using fixtures and clean assertions.
            """;
    }

    public string GetVerificationInstructions()
    {
        return """
            1. Execute the Python test suite using `RunCommand` in TerminalTools with command 'pytest' and arguments '-v'.
            2. If pytest fails, check imports or run 'flake8 .' to inspect syntax or lint issues.
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
