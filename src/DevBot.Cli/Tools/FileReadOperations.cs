using System.ComponentModel;
using System.Text;
using DevBot.Cli.Core;
using Microsoft.SemanticKernel;
using Spectre.Console;

namespace DevBot.Cli.Tools;

/// <summary>
/// Operaciones de lectura y exploración del sistema de archivos (SRP: sólo lectura).
/// Proporciona herramientas de inspección para los subagentes Scout, Planner y Reviewer.
/// </summary>
public class FileReadOperations : FileToolsBase
{
    public FileReadOperations(string repoRoot, AgentContext? context = null)
        : base(repoRoot, context)
    {
    }

    [KernelFunction, Description("Reads the content of a file in the repository, optionally within a line range (1-indexed).")]
    public string ReadFile(
        [Description("Relative path to the file within the repository")] string filePath,
        [Description("Optional 1-indexed starting line number")] int? startLine = null,
        [Description("Optional 1-indexed ending line number")] int? endLine = null)
    {
        try
        {
            string rangeInfo = startLine.HasValue || endLine.HasValue ? $" (líneas {startLine ?? 1}-{endLine?.ToString() ?? "fin"})" : "";
            AnsiConsole.MarkupLine($"[grey]  ↳ [cyan]ReadFile[/]:[/] [yellow]{Markup.Escape(filePath)}[/]{rangeInfo}");
            _context?.Metrics.RecordToolCall(nameof(ReadFile));
            _context?.Logger.LogEvent("FileTools", "ToolCall", $"ReadFile: '{filePath}'{rangeInfo}");

            string fullPath = ResolveSafePath(filePath);
            if (!File.Exists(fullPath))
            {
                _context?.Logger.LogEvent("FileTools", "Warning", $"ReadFile: File '{filePath}' does not exist.");
                return $"ERROR: File '{filePath}' does not exist.";
            }

            string[] lines = File.ReadAllLines(fullPath);
            int totalLines = lines.Length;

            int start = Math.Clamp(startLine ?? 1, 1, Math.Max(1, totalLines));
            int end = Math.Clamp(endLine ?? totalLines, start, Math.Max(start, totalLines));

            var sb = new StringBuilder();
            sb.AppendLine($"File: {GetRelativePath(fullPath)} (lines {start}-{end} of {totalLines})");
            sb.AppendLine("```");
            for (int i = start; i <= end; i++)
            {
                sb.AppendLine($"{i,5} | {lines[i - 1]}");
            }
            sb.AppendLine("```");
            _context?.Logger.LogEvent("FileTools", "ToolResult", $"ReadFile '{filePath}': {totalLines} lines read (range {start}-{end})");
            return OutputSanitizer.Sanitize(sb.ToString(), 16000);
        }
        catch (Exception ex)
        {
            _context?.Logger.LogEvent("FileTools", "Error", $"ReadFile error on '{filePath}': {ex.Message}");
            return $"ERROR reading file '{filePath}': {ex.Message}";
        }
    }

    [KernelFunction, Description("Lists files and directories in a given relative path, filtering out build, git, and package folders.")]
    public string ListFiles(
        [Description("Relative directory path within the repository, default is '.'")] string directoryPath = ".",
        [Description("Search pattern, e.g. '*' or '*.cs'")] string searchPattern = "*",
        [Description("Whether to list recursively")] bool recursive = true)
    {
        try
        {
            AnsiConsole.MarkupLine($"[grey]  ↳ [cyan]ListFiles[/]:[/] [yellow]{Markup.Escape(directoryPath)}[/] (patrón: {Markup.Escape(searchPattern)})");
            _context?.Metrics.RecordToolCall(nameof(ListFiles));
            _context?.Logger.LogEvent("FileTools", "ToolCall", $"ListFiles: '{directoryPath}' (pattern: '{searchPattern}')");

            string fullPath = ResolveSafePath(directoryPath);
            if (!Directory.Exists(fullPath))
            {
                _context?.Logger.LogEvent("FileTools", "Warning", $"ListFiles: Directory '{directoryPath}' does not exist.");
                return $"ERROR: Directory '{directoryPath}' does not exist.";
            }

            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            var allFiles = Directory.EnumerateFiles(fullPath, searchPattern, enumerationOptions)
                .Where(f => !IsIgnoredPath(f))
                .Select(f => GetRelativePath(f))
                .OrderBy(f => f)
                .ToList();

            _context?.Logger.LogEvent("FileTools", "ToolResult", $"ListFiles '{directoryPath}': found {allFiles.Count} files");

            if (allFiles.Count == 0)
            {
                return $"No files matching '{searchPattern}' found in '{directoryPath}'.";
            }

            var sb = new StringBuilder();
            const int maxDisplay = 50;
            int displayCount = Math.Min(maxDisplay, allFiles.Count);

            sb.AppendLine($"Files found in '{directoryPath}' (showing {displayCount} of {allFiles.Count} matching '{searchPattern}'):");
            for (int i = 0; i < displayCount; i++)
            {
                sb.AppendLine($"- {allFiles[i]}");
            }

            if (allFiles.Count > maxDisplay)
            {
                sb.AppendLine($"... ({allFiles.Count - maxDisplay} elementos omitidos. Usa ListFiles en subdirectorios específicos o SearchCode para ubicar archivos puntuales).");
            }

            return OutputSanitizer.Sanitize(sb.ToString(), 16000);
        }
        catch (Exception ex)
        {
            return $"ERROR listing files in '{directoryPath}': {ex.Message}";
        }
    }

    [KernelFunction, Description("Searches code files for a given text or pattern, returning matching files and snippets with line numbers.")]
    public async Task<string> SearchCode(
        [Description("Search query or text snippet")] string query,
        [Description("Semicolon-separated file patterns to include, e.g. '*.cs;*.json;*.md'")] string fileExtensions = "*.cs;*.json;*.md;*.txt;*.csproj;*.js;*.ts;*.html;*.css",
        CancellationToken cancellationToken = default)
    {
        try
        {
            AnsiConsole.MarkupLine($"[grey]  ↳ [cyan]SearchCode[/]:[/] \"[yellow]{Markup.Escape(query)}[/]\"");
            _context?.Metrics.RecordToolCall(nameof(SearchCode));
            _context?.Logger.LogEvent("FileTools", "ToolCall", $"SearchCode: \"{query}\"");

            if (string.IsNullOrWhiteSpace(query))
            {
                return "ERROR: Search query cannot be empty.";
            }

            var extensionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool matchAll = false;
            foreach (var part in fileExtensions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part == "*" || part == "*.*")
                {
                    matchAll = true;
                    break;
                }
                string ext = part.StartsWith("*.") ? part.Substring(1) : (part.StartsWith(".") ? part : "." + part);
                extensionSet.Add(ext);
            }

            const int maxMatches = 50;
            const long maxFileSizeBytes = 1024 * 1024; // 1 MB limit per file to prevent freeze

            var enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            var candidateFiles = Directory.EnumerateFiles(_repoRoot, "*", enumOptions)
                .Where(f => !IsIgnoredPath(f))
                .Where(file =>
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.Contains(".min.", StringComparison.OrdinalIgnoreCase) ||
                        fileName.EndsWith(".map", StringComparison.OrdinalIgnoreCase) ||
                        fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (!matchAll)
                    {
                        string ext = Path.GetExtension(file);
                        if (!extensionSet.Contains(ext)) return false;
                    }

                    return true;
                })
                .ToList();

            var results = new System.Collections.Concurrent.ConcurrentBag<string>();
            int matchCount = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(candidateFiles, parallelOptions, async (file, ct) =>
            {
                if (Volatile.Read(ref matchCount) >= maxMatches)
                {
                    return;
                }

                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length > maxFileSizeBytes) return;

                    var lines = await File.ReadAllLinesAsync(file, ct);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (Volatile.Read(ref matchCount) >= maxMatches) break;

                        string line = lines[i];
                        if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            int current = Interlocked.Increment(ref matchCount);
                            if (current <= maxMatches)
                            {
                                results.Add($"{GetRelativePath(file)}:{i + 1} | {line.Trim()}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal skipping unreadable file '{file}': {ex.Message}");
                }
            });

            _context?.Logger.LogEvent("FileTools", "ToolResult", $"SearchCode \"{query}\": {results.Count} occurrences found");

            if (results.IsEmpty)
            {
                return $"No matches found for '{query}'.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Matches for '{query}' (found {results.Count}):");
            foreach (var result in results.OrderBy(r => r))
            {
                sb.AppendLine($"- {result}");
            }
            return OutputSanitizer.Sanitize(sb.ToString(), 16000);
        }
        catch (Exception ex)
        {
            _context?.Logger.LogEvent("FileTools", "Error", $"SearchCode error on '{query}': {ex.Message}");
            return $"ERROR searching code for '{query}': {ex.Message}";
        }
    }
}
