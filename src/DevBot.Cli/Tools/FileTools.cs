using System.ComponentModel;
using DevBot.Cli.Core;
using DevBot.Cli.Core.Validation;
using Microsoft.SemanticKernel;

namespace DevBot.Cli.Tools;

/// <summary>
/// Representa un fragmento de cambio atómico para operaciones multi-diff.
/// </summary>
public class DiffChunk
{
    [Description("The exact original snippet of code to replace (must match exactly one location)")]
    public string OriginalSnippet { get; set; } = string.Empty;

    [Description("The new replacement code snippet")]
    public string ReplacementSnippet { get; set; } = string.Empty;
}

/// <summary>
/// Fachada unificada (Facade pattern) para operaciones sobre el sistema de archivos.
/// Coordina <see cref="FileReadOperations"/> y <see cref="FileWriteOperations"/> delegando
/// responsabilidades especializadas y manteniendo compatibilidad total con callers existentes.
/// </summary>
public class FileTools : FileToolsBase
{
    private readonly FileReadOperations _readOps;
    private readonly FileWriteOperations _writeOps;

    public FileTools(string repoRoot, AgentContext? context = null, ISyntaxValidator? syntaxValidator = null)
        : base(repoRoot, context)
    {
        _readOps = new FileReadOperations(repoRoot, context);
        _writeOps = new FileWriteOperations(repoRoot, context, syntaxValidator);
    }

    [KernelFunction, Description("Reads the content of a file in the repository, optionally within a line range (1-indexed).")]
    public string ReadFile(
        [Description("Relative path to the file within the repository")] string filePath,
        [Description("Optional 1-indexed starting line number")] int? startLine = null,
        [Description("Optional 1-indexed ending line number")] int? endLine = null)
        => _readOps.ReadFile(filePath, startLine, endLine);

    [KernelFunction, Description("Lists files and directories in a given relative path, filtering out build, git, and package folders.")]
    public string ListFiles(
        [Description("Relative directory path within the repository, default is '.'")] string directoryPath = ".",
        [Description("Search pattern, e.g. '*' or '*.cs'")] string searchPattern = "*",
        [Description("Whether to list recursively")] bool recursive = true)
        => _readOps.ListFiles(directoryPath, searchPattern, recursive);

    [KernelFunction, Description("Searches code files for a given text or pattern, returning matching files and snippets with line numbers.")]
    public string SearchCode(
        [Description("Search query or text snippet")] string query,
        [Description("Semicolon-separated file patterns to include, e.g. '*.cs;*.json;*.md'")] string fileExtensions = "*.cs;*.json;*.md;*.txt;*.csproj;*.js;*.ts;*.html;*.css")
        => _readOps.SearchCode(query, fileExtensions);

    [KernelFunction, Description("Writes full content to a file. Overwrites if existing, creates directories if missing.")]
    public string WriteFile(
        [Description("Relative path to the file to write")] string filePath,
        [Description("Complete content of the file")] string content)
        => _writeOps.WriteFile(filePath, content);

    [KernelFunction, Description("Applies a surgical code diff by replacing a unique snippet of code with replacement code in the target file.")]
    public string ApplyDiff(
        [Description("Relative path to the file to modify")] string filePath,
        [Description("The exact original snippet of code to replace (must match exactly one location)")] string originalSnippet,
        [Description("The new replacement code snippet")] string replacementSnippet)
        => _writeOps.ApplyDiff(filePath, originalSnippet, replacementSnippet);

    [KernelFunction, Description("Applies multiple non-contiguous surgical code diffs to a single file atomically. If any snippet fails to match or introduces syntax errors, no changes are applied.")]
    public string ApplyMultiDiff(
        [Description("Relative path to the file to modify")] string filePath,
        [Description("JSON array of diff objects containing 'originalSnippet' and 'replacementSnippet'. Example: [{\"originalSnippet\": \"...\", \"replacementSnippet\": \"...\"}]")] string diffsJson)
        => _writeOps.ApplyMultiDiff(filePath, diffsJson);

    public static KernelPlugin CreateScoutPlugin(string repoRoot, AgentContext? context = null)
        => ToolPluginFactory.CreateScoutPlugin(repoRoot, context);

    public static KernelPlugin CreateCoderPlugin(string repoRoot, AgentContext? context = null)
        => ToolPluginFactory.CreateCoderPlugin(repoRoot, context);

    public static KernelPlugin CreateReviewerPlugin(string repoRoot, AgentContext? context = null)
        => ToolPluginFactory.CreateReviewerPlugin(repoRoot, context);
}
