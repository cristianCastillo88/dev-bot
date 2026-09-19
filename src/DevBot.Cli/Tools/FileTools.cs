using System.ComponentModel;
using System.Reflection;
using System.Text;
using DevBot.Cli.Core;
using Microsoft.SemanticKernel;
using Spectre.Console;

namespace DevBot.Cli.Tools;

public class FileTools
{
    private readonly string _repoRoot;
    private readonly AgentContext? _context;
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".agent", ".vs", ".vscode", ".idea", ".fleet", ".github", ".husky", "dist", "build", "TestResults", "packages",
        ".next", "target", "vendor", "coverage", ".cache", "out", "artifacts", ".turbo"
    };

    public FileTools(string repoRoot, AgentContext? context = null)
    {
        _repoRoot = Path.GetFullPath(repoRoot);
        _context = context;
    }

    private string ResolveSafePath(string relativeOrFullPath)
    {
        string fullPath = Path.IsPathRooted(relativeOrFullPath)
            ? Path.GetFullPath(relativeOrFullPath)
            : Path.GetFullPath(Path.Combine(_repoRoot, relativeOrFullPath));

        if (!fullPath.StartsWith(_repoRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Access denied: '{relativeOrFullPath}' is outside the repository root.");
        }

        return fullPath;
    }

    private string GetRelativePath(string fullPath)
    {
        return Path.GetRelativePath(_repoRoot, fullPath).Replace('\\', '/');
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
            return SanitizeOutput(sb.ToString());
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

            return SanitizeOutput(sb.ToString());
        }
        catch (Exception ex)
        {
            return $"ERROR listing files in '{directoryPath}': {ex.Message}";
        }
    }

    [KernelFunction, Description("Searches code files for a given text or pattern, returning matching files and snippets with line numbers.")]
    public string SearchCode(
        [Description("Search query or text snippet")] string query,
        [Description("Semicolon-separated file patterns to include, e.g. '*.cs;*.json;*.md'")] string fileExtensions = "*.cs;*.json;*.md;*.txt;*.csproj;*.js;*.ts;*.html;*.css")
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

            var results = new List<string>();
            int matchCount = 0;
            const int maxMatches = 50;
            const long maxFileSizeBytes = 1024 * 1024; // 1 MB limit per file to prevent freeze

            var enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            var allFiles = Directory.EnumerateFiles(_repoRoot, "*", enumOptions)
                .Where(f => !IsIgnoredPath(f));

            foreach (var file in allFiles)
            {
                if (matchCount >= maxMatches) break;

                string fileName = Path.GetFileName(file);
                if (fileName.Contains(".min.", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".map", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!matchAll)
                {
                    string ext = Path.GetExtension(file);
                    if (!extensionSet.Contains(ext)) continue;
                }

                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length > maxFileSizeBytes) continue;

                    int lineNum = 1;
                    foreach (var line in File.ReadLines(file))
                    {
                        if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add($"{GetRelativePath(file)}:{lineNum} | {line.Trim()}");
                            matchCount++;
                            if (matchCount >= maxMatches) break;
                        }
                        lineNum++;
                    }
                }
                catch
                {
                    // Ignore unreadable or locked files
                }
            }

            _context?.Logger.LogEvent("FileTools", "ToolResult", $"SearchCode \"{query}\": {results.Count} occurrences found");

            if (results.Count == 0)
            {
                return $"No matches found for '{query}'.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Matches for '{query}' (found {results.Count}):");
            foreach (var result in results)
            {
                sb.AppendLine($"- {result}");
            }
            return SanitizeOutput(sb.ToString());
        }
        catch (Exception ex)
        {
            _context?.Logger.LogEvent("FileTools", "Error", $"SearchCode error on '{query}': {ex.Message}");
            return $"ERROR searching code for '{query}': {ex.Message}";
        }
    }

    [KernelFunction, Description("Writes full content to a file. Overwrites if existing, creates directories if missing.")]
    public string WriteFile(
        [Description("Relative path to the file to write")] string filePath,
        [Description("Complete content of the file")] string content)
    {
        try
        {
            AnsiConsole.MarkupLine($"[grey]  ↳ [cyan]WriteFile[/]:[/] [yellow]{Markup.Escape(filePath)}[/]");
            _context?.Metrics.RecordToolCall(nameof(WriteFile));
            _context?.Logger.LogEvent("FileTools", "ToolCall", $"WriteFile: '{filePath}' ({content.Length} chars)");

            string fullPath = ResolveSafePath(filePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(fullPath, content, Encoding.UTF8);
            string relPath = GetRelativePath(fullPath);
            _context?.ModifiedFiles.Add(relPath);
            _context?.Logger.LogEvent("FileTools", "ToolResult", $"WriteFile SUCCESS: '{relPath}' ({content.Length} chars)");
            return $"SUCCESS: Wrote {content.Length} characters to '{relPath}'.";
        }
        catch (Exception ex)
        {
            _context?.Logger.LogEvent("FileTools", "Error", $"WriteFile error on '{filePath}': {ex.Message}");
            return $"ERROR writing to file '{filePath}': {ex.Message}";
        }
    }

    [KernelFunction, Description("Applies a surgical code diff by replacing a unique snippet of code with replacement code in the target file.")]
    public string ApplyDiff(
        [Description("Relative path to the file to modify")] string filePath,
        [Description("The exact original snippet of code to replace (must match exactly one location)")] string originalSnippet,
        [Description("The new replacement code snippet")] string replacementSnippet)
    {
        try
        {
            AnsiConsole.MarkupLine($"[grey]  ↳ [cyan]ApplyDiff[/]:[/] [yellow]{Markup.Escape(filePath)}[/]");
            _context?.Metrics.RecordToolCall(nameof(ApplyDiff));
            _context?.Logger.LogEvent("FileTools", "ToolCall", $"ApplyDiff: '{filePath}'");

            string fullPath = ResolveSafePath(filePath);
            if (!File.Exists(fullPath))
            {
                _context?.Logger.LogEvent("FileTools", "Warning", $"ApplyDiff: File '{filePath}' does not exist.");
                return $"ERROR: File '{filePath}' does not exist.";
            }

            string content = File.ReadAllText(fullPath);
            bool hasCrLf = content.Contains("\r\n");

            // Normalize newlines to avoid line-ending mismatches (\r\n vs \n)
            string normalizedContent = content.Replace("\r\n", "\n");
            string normalizedTarget = originalSnippet.Replace("\r\n", "\n");
            string normalizedReplacement = replacementSnippet.Replace("\r\n", "\n");

            string newContent;
            int firstIndex = normalizedContent.IndexOf(normalizedTarget, StringComparison.Ordinal);

            if (firstIndex >= 0)
            {
                int secondIndex = normalizedContent.IndexOf(normalizedTarget, firstIndex + normalizedTarget.Length, StringComparison.Ordinal);
                if (secondIndex >= 0)
                {
                    _context?.Logger.LogEvent("FileTools", "Warning", $"ApplyDiff: snippet matches multiple locations in '{filePath}'.");
                    return $"ERROR: Original snippet matches multiple locations in '{filePath}'. Provide more surrounding context to make it unique.";
                }

                newContent = normalizedContent.Substring(0, firstIndex)
                            + normalizedReplacement
                            + normalizedContent.Substring(firstIndex + normalizedTarget.Length);
            }
            else
            {
                // Fallback: Line-trimmed flexible matching for slight whitespace/indentation variations
                var matchResult = TryLineTrimmedMatch(normalizedContent, normalizedTarget, normalizedReplacement);
                if (!matchResult.Success)
                {
                    _context?.Logger.LogEvent("FileTools", "Warning", $"ApplyDiff: snippet not found in '{filePath}'. Reason: {matchResult.ErrorMessage}");
                    return $"ERROR: Original snippet not found in '{filePath}'. {matchResult.ErrorMessage}";
                }
                newContent = matchResult.UpdatedContent!;
            }

            if (hasCrLf)
            {
                // Ensure consistent \r\n endings without double-CR (\r\r\n)
                newContent = newContent.Replace("\r\n", "\n").Replace("\n", "\r\n");
            }

            File.WriteAllText(fullPath, newContent, Encoding.UTF8);
            string relDiffPath = GetRelativePath(fullPath);
            _context?.ModifiedFiles.Add(relDiffPath);
            _context?.Logger.LogEvent("FileTools", "ToolResult", $"ApplyDiff SUCCESS: '{relDiffPath}'");
            return $"SUCCESS: Applied diff to '{relDiffPath}'.";
        }
        catch (Exception ex)
        {
            _context?.Logger.LogEvent("FileTools", "Error", $"ApplyDiff error on '{filePath}': {ex.Message}");
            return $"ERROR applying diff to '{filePath}': {ex.Message}";
        }
    }

    private static (bool Success, string? UpdatedContent, string? ErrorMessage) TryLineTrimmedMatch(
        string content,
        string target,
        string replacement)
    {
        var contentLines = content.Split('\n');
        var targetLines = target.Split('\n');

        // Trim blank lines at ends of target for matching
        int targetStart = 0;
        while (targetStart < targetLines.Length && string.IsNullOrWhiteSpace(targetLines[targetStart])) targetStart++;

        int targetEnd = targetLines.Length - 1;
        while (targetEnd >= targetStart && string.IsNullOrWhiteSpace(targetLines[targetEnd])) targetEnd--;

        if (targetStart > targetEnd)
        {
            return (false, null, "Target snippet contains only empty lines.");
        }

        int meaningfulCount = targetEnd - targetStart + 1;
        var trimmedTarget = new string[meaningfulCount];
        for (int i = 0; i < meaningfulCount; i++)
        {
            trimmedTarget[i] = targetLines[targetStart + i].Trim();
        }

        var matchIndices = new List<int>();

        for (int i = 0; i <= contentLines.Length - meaningfulCount; i++)
        {
            bool match = true;
            for (int j = 0; j < meaningfulCount; j++)
            {
                if (!string.Equals(contentLines[i + j].Trim(), trimmedTarget[j], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                matchIndices.Add(i);
            }
        }

        if (matchIndices.Count == 0)
        {
            return (false, null, "No matching lines found even with whitespace tolerance.");
        }

        if (matchIndices.Count > 1)
        {
            return (false, null, $"Snippet matches {matchIndices.Count} multiple locations with whitespace tolerance. Provide more surrounding context.");
        }

        int matchIdx = matchIndices[0];
        var resultLines = new List<string>();
        for (int i = 0; i < matchIdx; i++)
        {
            resultLines.Add(contentLines[i]);
        }

        resultLines.Add(replacement);

        for (int i = matchIdx + meaningfulCount; i < contentLines.Length; i++)
        {
            resultLines.Add(contentLines[i]);
        }

        return (true, string.Join("\n", resultLines), null);
    }

    private bool IsIgnoredPath(string fullPath)
    {
        string relative = GetRelativePath(fullPath);
        var parts = relative.Split('/', '\\');
        return parts.Any(p => IgnoredDirectories.Contains(p) || (p.StartsWith(".") && p != "." && p != ".."));
    }

    private static string SanitizeOutput(string output, int maxLength = 16000)
    {
        if (string.IsNullOrEmpty(output)) return string.Empty;

        var sb = new StringBuilder(Math.Min(output.Length, maxLength));
        foreach (char c in output)
        {
            if (c == '\r' || c == '\n' || c == '\t' || (!char.IsControl(c) && c != '\0'))
            {
                sb.Append(c);
            }
        }

        string clean = sb.ToString();
        if (clean.Length > maxLength)
        {
            return clean.Substring(0, maxLength) + "\n... [Contenido truncado para no exceder límites de carga útil] ...";
        }

        return clean;
    }

    // Factory methods to create strictly scoped plugins adhering to least privilege principle
    public static KernelPlugin CreateScoutPlugin(string repoRoot, AgentContext? context = null)
    {
        var tools = new FileTools(repoRoot, context);
        var functions = new List<KernelFunction>
        {
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileTools).GetMethod(nameof(ReadFile))!,
                tools,
                functionName: nameof(ReadFile),
                description: "Reads file content, optionally with startLine and endLine"),
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileTools).GetMethod(nameof(ListFiles))!,
                tools,
                functionName: nameof(ListFiles),
                description: "Lists files in directory ignoring build/git directories"),
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileTools).GetMethod(nameof(SearchCode))!,
                tools,
                functionName: nameof(SearchCode),
                description: "Searches code files for a string or pattern")
        };

        return KernelPluginFactory.CreateFromFunctions("FileTools", "Read-only file operations for codebase scouting", functions);
    }

    public static KernelPlugin CreateCoderPlugin(string repoRoot, AgentContext? context = null)
    {
        var tools = new FileTools(repoRoot, context);
        var functions = new List<KernelFunction>
        {
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileTools).GetMethod(nameof(ReadFile))!,
                tools,
                functionName: nameof(ReadFile),
                description: "Reads file content, optionally with startLine and endLine"),
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileTools).GetMethod(nameof(WriteFile))!,
                tools,
                functionName: nameof(WriteFile),
                description: "Writes full file content"),
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileTools).GetMethod(nameof(ApplyDiff))!,
                tools,
                functionName: nameof(ApplyDiff),
                description: "Applies a surgical code replacement in a file")
        };

        return KernelPluginFactory.CreateFromFunctions("FileTools", "Surgical code modification tools", functions);
    }

    public static KernelPlugin CreateReviewerPlugin(string repoRoot, AgentContext? context = null)
    {
        var tools = new FileTools(repoRoot, context);
        var functions = new List<KernelFunction>
        {
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileTools).GetMethod(nameof(ReadFile))!,
                tools,
                functionName: nameof(ReadFile),
                description: "Reads file content, optionally with startLine and endLine")
        };

        return KernelPluginFactory.CreateFromFunctions("FileTools", "Read-only file verification tools", functions);
    }
}
