using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DevBot.Cli.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.SemanticKernel;
using Spectre.Console;

namespace DevBot.Cli.Tools;

public class DiffChunk
{
    [Description("The exact original snippet of code to replace (must match exactly one location)")]
    public string OriginalSnippet { get; set; } = string.Empty;

    [Description("The new replacement code snippet")]
    public string ReplacementSnippet { get; set; } = string.Empty;
}

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

    private (bool Allowed, string? ErrorMessage) ValidateMilestoneScope(string filePath)
    {
        if (_context?.CurrentMilestone == null ||
            _context.CurrentMilestone.TargetFiles == null ||
            _context.CurrentMilestone.TargetFiles.Count == 0)
        {
            return (true, null);
        }

        string fullPath = ResolveSafePath(filePath);
        string relPath = GetRelativePath(fullPath).Replace('\\', '/');

        bool isMatch = _context.CurrentMilestone.TargetFiles.Any(tf =>
        {
            string normTf = tf.Trim().Replace('\\', '/');
            if (normTf.StartsWith("./")) normTf = normTf[2..];
            return normTf.Equals(relPath, StringComparison.OrdinalIgnoreCase) ||
                   relPath.EndsWith("/" + normTf, StringComparison.OrdinalIgnoreCase) ||
                   (normTf.EndsWith("/*") && relPath.StartsWith(normTf[..^2], StringComparison.OrdinalIgnoreCase));
        });

        if (!isMatch)
        {
            string targets = string.Join(", ", _context.CurrentMilestone.TargetFiles);
            return (false, $"SCOPE ERROR: Modifying '{relPath}' is blocked because it is not within the TargetFiles declared for Milestone #{_context.CurrentMilestone.Order} ('{_context.CurrentMilestone.Title}'). Allowed target files: [{targets}]. Confine your edits strictly to these files.");
        }

        return (true, null);
    }

    private (bool IsValid, string? ErrorMessage) ValidatePreFlightSyntax(string filePath, string content)
    {
        // 1. Delegar en la estrategia activa si existe un contexto configurado
        if (_context?.Strategy != null)
        {
            var terminal = new TerminalTools(_repoRoot, _context);
            var result = _context.Strategy.ValidateSyntaxAsync(filePath, content, terminal, CancellationToken.None).GetAwaiter().GetResult();
            return (result.IsValid, result.ErrorMessage);
        }

        // 2. Fallback de conveniencia para C# y JSON cuando FileTools se instancia sin contexto (ej. pruebas unitarias aisladas)
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
                return (true, null);
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(content);
            var diagnostics = syntaxTree.GetDiagnostics()
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

                return (false, $"C# syntax error(s): {errors}");
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
                return (false, $"JSON syntax error: {ex.Message}");
            }
        }

        return (true, null);
    }

    private static (bool Success, string UpdatedContent, string? ErrorMessage) ApplySingleSnippetReplacement(
        string normalizedContent,
        string originalSnippet,
        string replacementSnippet)
    {
        string normalizedTarget = originalSnippet.Replace("\r\n", "\n");
        string normalizedReplacement = replacementSnippet.Replace("\r\n", "\n");

        int firstIndex = normalizedContent.IndexOf(normalizedTarget, StringComparison.Ordinal);

        if (firstIndex >= 0)
        {
            int secondIndex = normalizedContent.IndexOf(normalizedTarget, firstIndex + normalizedTarget.Length, StringComparison.Ordinal);
            if (secondIndex >= 0)
            {
                return (false, normalizedContent, "Original snippet matches multiple locations in the file. Provide more surrounding context to make it unique.");
            }

            string newContent = normalizedContent.Substring(0, firstIndex)
                                + normalizedReplacement
                                + normalizedContent.Substring(firstIndex + normalizedTarget.Length);
            return (true, newContent, null);
        }

        // Fallback: Line-trimmed flexible matching for slight whitespace/indentation variations
        var matchResult = TryLineTrimmedMatch(normalizedContent, normalizedTarget, normalizedReplacement);
        if (!matchResult.Success)
        {
            return (false, normalizedContent, matchResult.ErrorMessage ?? "Snippet not found.");
        }

        return (true, matchResult.UpdatedContent!, null);
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

            var scopeCheck = ValidateMilestoneScope(filePath);
            if (!scopeCheck.Allowed)
            {
                _context?.Logger.LogEvent("FileTools", "Warning", scopeCheck.ErrorMessage!);
                return scopeCheck.ErrorMessage!;
            }

            var syntaxCheck = ValidatePreFlightSyntax(filePath, content);
            if (!syntaxCheck.IsValid)
            {
                _context?.Logger.LogEvent("FileTools", "Warning", $"WriteFile pre-flight syntax check failed on '{filePath}': {syntaxCheck.ErrorMessage}");
                return $"PRE-FLIGHT SYNTAX ERROR: Content contains syntax errors in '{filePath}'. Changes were NOT saved. Details: {syntaxCheck.ErrorMessage}. Please fix the syntax before writing.";
            }

            string fullPath = ResolveSafePath(filePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(fullPath, content, Encoding.UTF8);
            string relPath = GetRelativePath(fullPath);
            _context?.ModifiedFiles.Add(relPath);
            _context?.MilestoneModifiedFiles.Add(relPath);
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

            var scopeCheck = ValidateMilestoneScope(filePath);
            if (!scopeCheck.Allowed)
            {
                _context?.Logger.LogEvent("FileTools", "Warning", scopeCheck.ErrorMessage!);
                return scopeCheck.ErrorMessage!;
            }

            string fullPath = ResolveSafePath(filePath);
            if (!File.Exists(fullPath))
            {
                _context?.Logger.LogEvent("FileTools", "Warning", $"ApplyDiff: File '{filePath}' does not exist.");
                return $"ERROR: File '{filePath}' does not exist.";
            }

            string content = File.ReadAllText(fullPath);
            bool hasCrLf = content.Contains("\r\n");
            string normalizedContent = content.Replace("\r\n", "\n");

            var replacementResult = ApplySingleSnippetReplacement(normalizedContent, originalSnippet, replacementSnippet);
            if (!replacementResult.Success)
            {
                _context?.Logger.LogEvent("FileTools", "Warning", $"ApplyDiff: failed on '{filePath}'. Reason: {replacementResult.ErrorMessage}");
                return $"ERROR: {replacementResult.ErrorMessage}";
            }

            string newContent = replacementResult.UpdatedContent;
            if (hasCrLf)
            {
                newContent = newContent.Replace("\r\n", "\n").Replace("\n", "\r\n");
            }

            var syntaxCheck = ValidatePreFlightSyntax(filePath, newContent);
            if (!syntaxCheck.IsValid)
            {
                _context?.Logger.LogEvent("FileTools", "Warning", $"ApplyDiff pre-flight syntax check failed on '{filePath}': {syntaxCheck.ErrorMessage}");
                return $"PRE-FLIGHT SYNTAX ERROR: The proposed diff would introduce syntax errors in '{filePath}'. Changes were NOT saved. Details: {syntaxCheck.ErrorMessage}. Please fix your replacement snippet.";
            }

            File.WriteAllText(fullPath, newContent, Encoding.UTF8);
            string relDiffPath = GetRelativePath(fullPath);
            _context?.ModifiedFiles.Add(relDiffPath);
            _context?.MilestoneModifiedFiles.Add(relDiffPath);
            _context?.Logger.LogEvent("FileTools", "ToolResult", $"ApplyDiff SUCCESS: '{relDiffPath}'");
            return $"SUCCESS: Applied diff to '{relDiffPath}'.";
        }
        catch (Exception ex)
        {
            _context?.Logger.LogEvent("FileTools", "Error", $"ApplyDiff error on '{filePath}': {ex.Message}");
            return $"ERROR applying diff to '{filePath}': {ex.Message}";
        }
    }

    [KernelFunction, Description("Applies multiple non-contiguous surgical code diffs to a single file atomically. If any snippet fails to match or introduces syntax errors, no changes are applied.")]
    public string ApplyMultiDiff(
        [Description("Relative path to the file to modify")] string filePath,
        [Description("JSON array of diff objects containing 'originalSnippet' and 'replacementSnippet'. Example: [{\"originalSnippet\": \"...\", \"replacementSnippet\": \"...\"}]")] string diffsJson)
    {
        try
        {
            AnsiConsole.MarkupLine($"[grey]  ↳ [cyan]ApplyMultiDiff[/]:[/] [yellow]{Markup.Escape(filePath)}[/]");
            _context?.Metrics.RecordToolCall(nameof(ApplyMultiDiff));
            _context?.Logger.LogEvent("FileTools", "ToolCall", $"ApplyMultiDiff: '{filePath}'");

            var scopeCheck = ValidateMilestoneScope(filePath);
            if (!scopeCheck.Allowed)
            {
                _context?.Logger.LogEvent("FileTools", "Warning", scopeCheck.ErrorMessage!);
                return scopeCheck.ErrorMessage!;
            }

            List<DiffChunk>? chunks;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                chunks = JsonSerializer.Deserialize<List<DiffChunk>>(diffsJson, options);
            }
            catch (Exception ex)
            {
                _context?.Logger.LogEvent("FileTools", "Warning", $"ApplyMultiDiff JSON parse error on '{filePath}': {ex.Message}");
                return $"ERROR: Failed to parse 'diffsJson'. Ensure it is a valid JSON array of objects with 'originalSnippet' and 'replacementSnippet'. Details: {ex.Message}";
            }

            if (chunks == null || chunks.Count == 0)
            {
                return "ERROR: No diff chunks provided in 'diffsJson'.";
            }

            string fullPath = ResolveSafePath(filePath);
            if (!File.Exists(fullPath))
            {
                _context?.Logger.LogEvent("FileTools", "Warning", $"ApplyMultiDiff: File '{filePath}' does not exist.");
                return $"ERROR: File '{filePath}' does not exist.";
            }

            string content = File.ReadAllText(fullPath);
            bool hasCrLf = content.Contains("\r\n");
            string currentContent = content.Replace("\r\n", "\n");

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (string.IsNullOrEmpty(chunk.OriginalSnippet))
                {
                    return $"ERROR in chunk #{i + 1}: 'originalSnippet' is empty. Transaction aborted without changes.";
                }

                var stepResult = ApplySingleSnippetReplacement(currentContent, chunk.OriginalSnippet, chunk.ReplacementSnippet ?? string.Empty);
                if (!stepResult.Success)
                {
                    _context?.Logger.LogEvent("FileTools", "Warning", $"ApplyMultiDiff chunk #{i + 1} failed on '{filePath}': {stepResult.ErrorMessage}");
                    return $"TRANSACTION ABORTED: Chunk #{i + 1} of {chunks.Count} failed: {stepResult.ErrorMessage}. No changes were applied to '{filePath}'.";
                }

                currentContent = stepResult.UpdatedContent;
            }

            if (hasCrLf)
            {
                currentContent = currentContent.Replace("\r\n", "\n").Replace("\n", "\r\n");
            }

            var syntaxCheck = ValidatePreFlightSyntax(filePath, currentContent);
            if (!syntaxCheck.IsValid)
            {
                _context?.Logger.LogEvent("FileTools", "Warning", $"ApplyMultiDiff pre-flight syntax check failed on '{filePath}': {syntaxCheck.ErrorMessage}");
                return $"PRE-FLIGHT SYNTAX ERROR: The proposed multi-diff would introduce syntax errors in '{filePath}'. Changes were NOT saved. Details: {syntaxCheck.ErrorMessage}. Please fix your replacement snippets.";
            }

            File.WriteAllText(fullPath, currentContent, Encoding.UTF8);
            string relDiffPath = GetRelativePath(fullPath);
            _context?.ModifiedFiles.Add(relDiffPath);
            _context?.MilestoneModifiedFiles.Add(relDiffPath);
            _context?.Logger.LogEvent("FileTools", "ToolResult", $"ApplyMultiDiff SUCCESS: '{relDiffPath}' ({chunks.Count} chunks)");
            return $"SUCCESS: Atomically applied {chunks.Count} diff chunks to '{relDiffPath}'.";
        }
        catch (Exception ex)
        {
            _context?.Logger.LogEvent("FileTools", "Error", $"ApplyMultiDiff error on '{filePath}': {ex.Message}");
            return $"ERROR applying multi-diff to '{filePath}': {ex.Message}";
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
                description: "Applies a surgical code replacement in a file"),
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileTools).GetMethod(nameof(ApplyMultiDiff))!,
                tools,
                functionName: nameof(ApplyMultiDiff),
                description: "Applies multiple non-contiguous surgical code diffs to a single file atomically")
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
