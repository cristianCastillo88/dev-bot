using System.ComponentModel;
using System.Text;
using System.Text.Json;
using DevBot.Cli.Core;
using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Core.Validation;
using Microsoft.SemanticKernel;
using Spectre.Console;

namespace DevBot.Cli.Tools;

/// <summary>
/// Operaciones de escritura, reemplazo y modificación quirúrgica de archivos (SRP: sólo escritura).
/// Utilizado principalmente por el subagente Coder con validaciones pre-flight de sintaxis y scope de hitos.
/// </summary>
public class FileWriteOperations : FileToolsBase
{
    private readonly ISyntaxValidator _syntaxValidator;

    public FileWriteOperations(
        string repoRoot,
        AgentContext? context = null,
        ISyntaxValidator? syntaxValidator = null)
        : base(repoRoot, context)
    {
        _syntaxValidator = syntaxValidator ?? new RoslynSyntaxValidator();
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

    public (bool Allowed, string? ErrorMessage) ValidateMilestoneScope(string filePath)
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

    public (bool IsValid, string? ErrorMessage) ValidatePreFlightSyntax(string filePath, string content)
    {
        if (_context?.Strategy != null && _context.Strategy is not DotNetStrategy)
        {
            var terminal = new TerminalTools(_repoRoot, _context);
            var result = _context.Strategy.ValidateSyntaxAsync(filePath, content, terminal, CancellationToken.None).GetAwaiter().GetResult();
            return (result.IsValid, result.ErrorMessage);
        }

        var check = _syntaxValidator.Validate(filePath, content);
        return (check.IsValid, check.ErrorMessage);
    }

    public static (bool Success, string UpdatedContent, string? ErrorMessage) ApplySingleSnippetReplacement(
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

        var matchResult = TryLineTrimmedMatch(normalizedContent, normalizedTarget, normalizedReplacement);
        if (!matchResult.Success)
        {
            return (false, normalizedContent, matchResult.ErrorMessage ?? "Snippet not found.");
        }

        return (true, matchResult.UpdatedContent!, null);
    }

    private static (bool Success, string? UpdatedContent, string? ErrorMessage) TryLineTrimmedMatch(
        string content,
        string target,
        string replacement)
    {
        var contentLines = content.Split('\n');
        var targetLines = target.Split('\n');

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
}
