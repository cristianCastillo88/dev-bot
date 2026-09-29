using System.Text.Json;
using DevBot.Cli.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

    public Task<SyntaxValidationResult> ValidateSyntaxAsync(
        string filePath,
        string content,
        TerminalTools terminal,
        CancellationToken ct = default)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".cs")
        {
            bool hasDeclarations = content.Contains("class ") ||
                                   content.Contains("record ") ||
                                   content.Contains("interface ") ||
                                   content.Contains("struct ") ||
                                   content.Contains("namespace ") ||
                                   content.Contains("enum ");

            if (!hasDeclarations)
            {
                return Task.FromResult(SyntaxValidationResult.Success());
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(content, cancellationToken: ct);
            var diagnostics = syntaxTree.GetDiagnostics(ct)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(3)
                .ToList();

            if (diagnostics.Count > 0)
            {
                var errors = string.Join("; ", diagnostics.Select(d =>
                {
                    var lineSpan = d.Location.GetLineSpan();
                    int line = lineSpan.StartLinePosition.Line + 1;
                    return $"Línea {line}: {d.GetMessage()}";
                }));

                return Task.FromResult(SyntaxValidationResult.Failure($"C# syntax error(s): {errors}"));
            }
        }
        else if (ext == ".json")
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
            }
            catch (JsonException ex)
            {
                return Task.FromResult(SyntaxValidationResult.Failure($"JSON syntax error: {ex.Message}"));
            }
        }

        return Task.FromResult(SyntaxValidationResult.Success());
    }

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
        // Auto-formatea los archivos para alinearlos con las reglas oficiales de C# de manera transparente
        string args = string.IsNullOrWhiteSpace(_targetFile)
            ? "format"
            : $"format \"{_targetFile}\"";

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
        string targetArg = !string.IsNullOrWhiteSpace(_targetFile) ? $" \"{_targetFile}\"" : string.Empty;
        string locationNote = !string.IsNullOrWhiteSpace(_targetFile)
            ? $"\nIMPORTANT: The project or solution file is located at '{_targetFile}'. You MUST run commands targeting this exact file: `dotnet test \"{_targetFile}\"`\n"
            : string.Empty;

        return $"""
            1. Execute the .NET test suite using `RunCommand` in TerminalTools with command: dotnet test{targetArg}
            2. If there are compilation or build errors, run: dotnet build{targetArg} to inspect detailed diagnostic messages.
            3. Verify clean code and formatting with: dotnet format --verify-no-changes{targetArg} if supported.
            {locationNote}
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
