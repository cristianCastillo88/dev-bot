using DevBot.Cli.Tools;

namespace DevBot.Cli.Core.Strategies;

public class NodeNpmStrategy : IBuildAndTestStrategy
{
    public ProjectStackType SupportedStack => ProjectStackType.NodeJs;
    public string Name => "Node.js / TypeScript / JavaScript Strategy";

    public async Task<StrategyExecutionResult> BuildAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string raw = await terminal.RunCommand("npm", "run build");
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, "npm run build");
    }

    public async Task<StrategyExecutionResult> TestAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string raw = await terminal.RunCommand("npm", "test");
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, "npm test");
    }

    public async Task<StrategyExecutionResult> LintOrFormatAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string raw = await terminal.RunCommand("npm", "run lint");
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, "npm run lint");
    }

    public string GetIdiomaticPromptGuidelines()
    {
        return """
            [TYPESCRIPT / JAVASCRIPT IDIOMATIC CONVENTIONS]
            - Use modern TypeScript / ES2022+ syntax (explicit types, interfaces/types, async/await, optional chaining).
            - Avoid `any` types; prefer union types, generics, or `unknown` with type guards.
            - Ensure clean exports/imports adhering to the project's module system (ESM or CommonJS).
            - Handle Promise rejections and errors with try/catch or typed result objects.
            - Write tests using Jest, Vitest, or Mocha adhering to existing testing conventions.
            """;
    }

    public string GetVerificationInstructions()
    {
        return """
            1. Execute the project test suite using `RunCommand` in TerminalTools with command 'npm test' (or command 'npm' and arguments 'test').
            2. If TypeScript compilation errors occur, run 'npx tsc --noEmit' to inspect type errors.
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
