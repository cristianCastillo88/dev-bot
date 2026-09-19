using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using DevBot.Cli.Core;
using Microsoft.SemanticKernel;
using Spectre.Console;

namespace DevBot.Cli.Tools;

public class TerminalTools
{
    private readonly string _defaultWorkingDirectory;
    private readonly AgentContext? _context;

    public TerminalTools(string defaultWorkingDirectory, AgentContext? context = null)
    {
        _defaultWorkingDirectory = Path.GetFullPath(defaultWorkingDirectory);
        _context = context;
    }

    [KernelFunction, Description("Executes a terminal/console command (e.g. 'dotnet test', 'npm test') and captures its stdout, stderr, and exit code.")]
    public async Task<string> RunCommand(
        [Description("The command to execute, e.g. 'dotnet' or 'npm' or 'git'")] string command,
        [Description("Arguments to pass to the command, e.g. 'test --no-build'")] string arguments = "",
        [Description("Optional relative working directory. Defaults to repository root.")] string? workingDirectory = null,
        [Description("Timeout in seconds before terminating the process (default 120)")] int timeoutSeconds = 120)
    {
        AnsiConsole.MarkupLine($"[grey]  ↳ [cyan]RunCommand[/]:[/] [yellow]{Markup.Escape(command)} {Markup.Escape(arguments)}[/]");
        _context?.Metrics.RecordToolCall(nameof(RunCommand));
        _context?.Logger.LogEvent("TerminalTools", "ToolCall", $"RunCommand: {command} {arguments}");

        string workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? _defaultWorkingDirectory
            : Path.GetFullPath(Path.Combine(_defaultWorkingDirectory, workingDirectory));

        if (!Directory.Exists(workDir))
        {
            return $"ERROR: Working directory '{workDir}' does not exist.";
        }

        string fullCommandLine = string.IsNullOrWhiteSpace(arguments) ? command : $"{command} {arguments}";
        bool containsShellOperator = fullCommandLine.Contains("&&") || fullCommandLine.Contains("||") ||
                                     fullCommandLine.Contains("|") || fullCommandLine.Contains(">") ||
                                     fullCommandLine.Contains("<");

        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (containsShellOperator)
        {
            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = $"/c \"{fullCommandLine}\"";
            }
            else
            {
                psi.FileName = "/bin/sh";
                psi.Arguments = $"-c \"{fullCommandLine.Replace("\"", "\\\"")}\"";
            }
        }
        else
        {
            psi.FileName = command;
            psi.Arguments = arguments;

            // If on Windows and command isn't a direct executable or path, resolve common script wrappers
            if (OperatingSystem.IsWindows() &&
                !command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) &&
                !command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                var cmdTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "npm", "npx", "yarn", "pnpm", "tsc", "pip", "pytest" };
                if (cmdTools.Contains(command))
                {
                    psi.FileName = $"{command}.cmd";
                }
            }
        }

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using var process = new Process { StartInfo = psi };

        try
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            try
            {
                // Close standard input immediately so process does not hang waiting for interactive input
                process.StandardInput.Close();
            }
            catch { }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }

                _context?.RecordCommandExecution(fullCommandLine, -1);
                _context?.Logger.LogEvent("TerminalTools", "Error", $"RunCommand '{command} {arguments}' timed out after {timeoutSeconds}s.");
                return $"COMMAND TIMEOUT: The command '{command} {arguments}' exceeded the {timeoutSeconds}s timeout and was killed.\n\nPartial Output:\n{outputBuilder}\n\nErrors:\n{errorBuilder}";
            }

            int exitCode = process.ExitCode;
            _context?.RecordCommandExecution(fullCommandLine, exitCode);
            string stdout = outputBuilder.ToString().Trim();
            string stderr = errorBuilder.ToString().Trim();

            _context?.Logger.LogEvent("TerminalTools", "ToolResult", $"RunCommand '{command} {arguments}' completed with exit code {exitCode}");

            var result = new StringBuilder();
            result.AppendLine($"Exit Code: {exitCode}");
            if (!string.IsNullOrEmpty(stdout))
            {
                result.AppendLine("--- STDOUT ---");
                // Limit very long outputs to avoid context flooding
                if (stdout.Length > 8000)
                {
                    result.AppendLine(stdout.Substring(0, 4000));
                    result.AppendLine("\n... [Output truncated to conserve tokens] ...\n");
                    result.AppendLine(stdout.Substring(stdout.Length - 4000));
                }
                else
                {
                    result.AppendLine(stdout);
                }
            }

            if (!string.IsNullOrEmpty(stderr))
            {
                result.AppendLine("--- STDERR ---");
                if (stderr.Length > 4000)
                {
                    result.AppendLine(stderr.Substring(0, 2000));
                    result.AppendLine("\n... [Stderr truncated] ...\n");
                    result.AppendLine(stderr.Substring(stderr.Length - 2000));
                }
                else
                {
                    result.AppendLine(stderr);
                }
            }

            string rawResult = result.ToString();
            var clean = new StringBuilder(rawResult.Length);
            foreach (char c in rawResult)
            {
                if (c == '\r' || c == '\n' || c == '\t' || (!char.IsControl(c) && c != '\0'))
                {
                    clean.Append(c);
                }
            }
            return clean.ToString();
        }
        catch (Exception ex)
        {
            _context?.Logger.LogEvent("TerminalTools", "Error", $"RunCommand '{command} {arguments}' threw exception: {ex.Message}");
            return $"ERROR executing command '{command} {arguments}': {ex.Message}";
        }
    }
}
