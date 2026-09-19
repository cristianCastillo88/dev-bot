using DevBot.Cli.Tools;

namespace DevBot.Cli.Core.Strategies;

public class MavenStrategy : IBuildAndTestStrategy
{
    public ProjectStackType SupportedStack => ProjectStackType.JavaMaven;
    public string Name => "Java / Maven Strategy";

    public async Task<StrategyExecutionResult> BuildAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string raw = await terminal.RunCommand("mvn", "compile -B");
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, "mvn compile -B");
    }

    public async Task<StrategyExecutionResult> TestAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string raw = await terminal.RunCommand("mvn", "test -B");
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, "mvn test -B");
    }

    public async Task<StrategyExecutionResult> LintOrFormatAsync(TerminalTools terminal, CancellationToken ct = default)
    {
        string raw = await terminal.RunCommand("mvn", "checkstyle:check -B");
        int exitCode = ParseExitCode(raw);
        return new StrategyExecutionResult(exitCode == 0, exitCode, raw, exitCode != 0 ? raw : string.Empty, "mvn checkstyle:check -B");
    }

    public string GetIdiomaticPromptGuidelines()
    {
        return """
            [JAVA / MAVEN IDIOMATIC CONVENTIONS]
            - Use modern Java idioms (records, pattern matching, streams, Optional).
            - Respect standard packaging conventions (`com.company.project.layer`).
            - Use Dependency Injection (Spring Boot `@Service`, `@Component`, `@Autowired` constructor injection).
            - Write JUnit 5 / AssertJ unit tests with clear mock configurations (Mockito).
            """;
    }

    public string GetVerificationInstructions()
    {
        return """
            1. Execute the Maven test suite using `RunCommand` in TerminalTools with command 'mvn test' (or command 'mvn' and arguments 'test -B').
            2. If compilation issues arise, run 'mvn compile -B' to diagnose syntax or missing dependencies.
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
