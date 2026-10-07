using DevBot.Cli.Core;
using DevBot.Cli.Core.Validation;
using Microsoft.SemanticKernel;

namespace DevBot.Cli.Tools;

/// <summary>
/// Fábrica centralizada de plugins para Semantic Kernel basada en el principio de mínimo privilegio.
/// Ensambla únicamente las funciones que cada subagente requiere según su rol.
/// </summary>
public static class ToolPluginFactory
{
    /// <summary>
    /// Crea el plugin de sólo lectura para exploración del repositorio (Scout y Planner).
    /// </summary>
    public static KernelPlugin CreateScoutPlugin(string repoRoot, AgentContext? context = null)
    {
        var readOps = new FileReadOperations(repoRoot, context);
        var functions = new List<KernelFunction>
        {
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileReadOperations).GetMethod(nameof(FileReadOperations.ReadFile))!,
                readOps,
                functionName: nameof(FileReadOperations.ReadFile),
                description: "Reads file content, optionally with startLine and endLine"),
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileReadOperations).GetMethod(nameof(FileReadOperations.ListFiles))!,
                readOps,
                functionName: nameof(FileReadOperations.ListFiles),
                description: "Lists files in directory ignoring build/git directories"),
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileReadOperations).GetMethod(nameof(FileReadOperations.SearchCode))!,
                readOps,
                functionName: nameof(FileReadOperations.SearchCode),
                description: "Searches code files for a string or pattern")
        };

        return KernelPluginFactory.CreateFromFunctions("FileTools", "Read-only file operations for codebase scouting", functions);
    }

    /// <summary>
    /// Crea el plugin de modificación de código para el subagente Coder.
    /// Combina lectura segura con edición quirúrgica y validaciones de compilación.
    /// </summary>
    public static KernelPlugin CreateCoderPlugin(string repoRoot, AgentContext? context = null, ISyntaxValidator? syntaxValidator = null)
    {
        var readOps = new FileReadOperations(repoRoot, context);
        var writeOps = new FileWriteOperations(repoRoot, context, syntaxValidator);

        var functions = new List<KernelFunction>
        {
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileReadOperations).GetMethod(nameof(FileReadOperations.ReadFile))!,
                readOps,
                functionName: nameof(FileReadOperations.ReadFile),
                description: "Reads file content, optionally with startLine and endLine"),
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileWriteOperations).GetMethod(nameof(FileWriteOperations.WriteFile))!,
                writeOps,
                functionName: nameof(FileWriteOperations.WriteFile),
                description: "Writes full file content"),
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileWriteOperations).GetMethod(nameof(FileWriteOperations.ApplyDiff))!,
                writeOps,
                functionName: nameof(FileWriteOperations.ApplyDiff),
                description: "Applies a surgical code replacement in a file"),
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileWriteOperations).GetMethod(nameof(FileWriteOperations.ApplyMultiDiff))!,
                writeOps,
                functionName: nameof(FileWriteOperations.ApplyMultiDiff),
                description: "Applies multiple non-contiguous surgical code diffs to a single file atomically")
        };

        return KernelPluginFactory.CreateFromFunctions("FileTools", "Surgical code modification tools", functions);
    }

    /// <summary>
    /// Crea el plugin restringido para el subagente Reviewer (sólo verificación de código modificado).
    /// </summary>
    public static KernelPlugin CreateReviewerPlugin(string repoRoot, AgentContext? context = null)
    {
        var readOps = new FileReadOperations(repoRoot, context);
        var functions = new List<KernelFunction>
        {
            KernelFunctionFactory.CreateFromMethod(
                typeof(FileReadOperations).GetMethod(nameof(FileReadOperations.ReadFile))!,
                readOps,
                functionName: nameof(FileReadOperations.ReadFile),
                description: "Reads file content, optionally with startLine and endLine")
        };

        return KernelPluginFactory.CreateFromFunctions("FileTools", "Read-only file verification tools", functions);
    }
}
