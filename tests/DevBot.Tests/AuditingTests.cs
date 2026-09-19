using DevBot.Cli.Core.Auditing;
using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Tools;
using Xunit;

namespace DevBot.Tests;

public class AuditingTests : IDisposable
{
    private readonly string _testDir;
    private readonly SecretScanner _scanner = new();

    public AuditingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "devbot_audit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Fact]
    public void SecretScanner_DetectsGoogleApiKey()
    {
        string content = "string apiKey = \"AIzaSyD-1234567890abcdefghijklmnopqrstuv\";";
        var findings = _scanner.ScanContent("Program.cs", content);

        Assert.Single(findings);
        Assert.Equal("SEC001", findings[0].RuleId);
        Assert.Contains("****", findings[0].RedactedSnippet);
        Assert.DoesNotContain("1234567890", findings[0].RedactedSnippet);
    }

    [Fact]
    public void SecretScanner_DetectsOpenAiKey()
    {
        string content = "var key = \"sk-abc1234567890defghijklmnopqrstuvwx\";";
        var findings = _scanner.ScanContent("appsettings.json", content);

        Assert.Single(findings);
        Assert.Equal("SEC002", findings[0].RuleId);
    }

    [Fact]
    public void SecretScanner_DetectsGitHubToken()
    {
        string content = "export GITHUB_TOKEN=ghp_1234567890abcdefghijklmnopqrstuvwxyz";
        var findings = _scanner.ScanContent(".env", content);

        Assert.Single(findings);
        Assert.Equal("SEC003", findings[0].RuleId);
    }

    [Fact]
    public void SecretScanner_DetectsAwsKey()
    {
        string content = "aws_access_key_id = AKIAIOSFODNN7EXAMPLE";
        var findings = _scanner.ScanContent("credentials", content);

        Assert.Single(findings);
        Assert.Equal("SEC004", findings[0].RuleId);
    }

    [Fact]
    public void SecretScanner_DetectsJwt()
    {
        string content = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgN_sample_jwt_signature_here_12345";
        var findings = _scanner.ScanContent("auth.log", content);

        Assert.Single(findings);
        Assert.Equal("SEC005", findings[0].RuleId);
    }

    [Fact]
    public void SecretScanner_DetectsPemPrivateKey()
    {
        string content = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA0...\n-----END RSA PRIVATE KEY-----";
        var findings = _scanner.ScanContent("server.key", content);

        Assert.Single(findings);
        Assert.Equal("SEC006", findings[0].RuleId);
    }

    [Fact]
    public void SecretScanner_DetectsConnectionStringPassword()
    {
        string content = "Server=myServerAddress;Database=myDataBase;Uid=myUsername;Pwd=SuperSecretPassword123!;";
        var findings = _scanner.ScanContent("appsettings.json", content);

        Assert.Single(findings);
        Assert.Equal("SEC007", findings[0].RuleId);
    }

    [Fact]
    public void SecretScanner_DoesNotFlagNormalCode()
    {
        string content = """
            using System;
            namespace MyApp;
            public class Calculator
            {
                public int Add(int a, int b) => a + b;
                private readonly string _name = "test-calculator";
            }
            """;

        var findings = _scanner.ScanContent("Calculator.cs", content);
        Assert.Empty(findings);
    }

    [Fact]
    public void RedactSecret_HidesSensitivePortion()
    {
        string secret = "AIzaSyD-1234567890abcdefghijklmnopqrstuv";
        string redacted = SecretScanner.RedactSecret(secret);

        Assert.StartsWith("AIz", redacted);
        Assert.EndsWith("uv", redacted);
        Assert.Contains("****...****", redacted);
        Assert.DoesNotContain("1234567890", redacted);
    }

    [Fact]
    public async Task PreCommitAuditor_BlocksCommit_WhenSecretDetected()
    {
        string badFile = Path.Combine(_testDir, "Config.cs");
        await File.WriteAllTextAsync(badFile, "string key = \"AIzaSyD-1234567890abcdefghijklmnopqrstuv\";");

        var auditor = new PreCommitAuditor();
        var strategy = new GenericFallbackStrategy();
        var terminal = new TerminalTools(_testDir);

        var result = await auditor.AuditAsync(_testDir, new[] { "Config.cs" }, strategy, terminal);

        Assert.False(result.Passed);
        Assert.Single(result.SecurityFindings);
        Assert.Contains("FALLO DE SEGURIDAD", result.Summary);
    }

    [Fact]
    public async Task PreCommitAuditor_Passes_WhenLinterFailsDueToMissingSolution()
    {
        string cleanFile = Path.Combine(_testDir, "Clean.cs");
        await File.WriteAllTextAsync(cleanFile, "public class Clean { public int X = 42; }");

        var auditor = new PreCommitAuditor();
        // DotNetStrategy simulada que falla con FileNotFoundException de MSBuild por falta de solución
        var mockStrategy = new MockFailingLinterStrategy("System.IO.FileNotFoundException: No se encontró ningún archivo del proyecto ni archivo de solución MSBuild en \"C:\\Repo\\\"");
        var terminal = new TerminalTools(_testDir);

        var result = await auditor.AuditAsync(_testDir, new[] { "Clean.cs" }, mockStrategy, terminal);

        Assert.True(result.Passed);
        Assert.Empty(result.SecurityFindings);
        Assert.Contains("omitido", result.Summary, StringComparison.OrdinalIgnoreCase);
    }
}

public class MockFailingLinterStrategy : IBuildAndTestStrategy
{
    private readonly string _error;
    public MockFailingLinterStrategy(string error) => _error = error;
    public ProjectStackType SupportedStack => ProjectStackType.DotNet;
    public string Name => "Mock .NET";
    public Task<StrategyExecutionResult> BuildAsync(TerminalTools terminal, CancellationToken ct = default) => Task.FromResult(new StrategyExecutionResult(true, 0, "", "", ""));
    public Task<StrategyExecutionResult> TestAsync(TerminalTools terminal, CancellationToken ct = default) => Task.FromResult(new StrategyExecutionResult(true, 0, "", "", ""));
    public Task<StrategyExecutionResult> LintOrFormatAsync(TerminalTools terminal, CancellationToken ct = default) => Task.FromResult(new StrategyExecutionResult(false, 1, "", _error, "dotnet format"));
    public string GetIdiomaticPromptGuidelines() => "";
    public string GetVerificationInstructions() => "";
}
