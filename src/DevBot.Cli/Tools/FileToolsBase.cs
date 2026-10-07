using DevBot.Cli.Core;

namespace DevBot.Cli.Tools;

/// <summary>
/// Clase base con utilidades de resolución de rutas, seguridad de acceso y directorios ignorados.
/// Responsabilidad única: validar y normalizar paths dentro del repositorio.
/// </summary>
public abstract class FileToolsBase
{
    protected readonly string _repoRoot;
    protected readonly AgentContext? _context;

    public static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".agent", ".vs", ".vscode", ".idea", ".fleet", ".github", ".husky", "dist", "build", "TestResults", "packages",
        ".next", "target", "vendor", "coverage", ".cache", "out", "artifacts", ".turbo"
    };

    protected FileToolsBase(string repoRoot, AgentContext? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        _repoRoot = Path.GetFullPath(repoRoot);
        _context = context;
    }

    /// <summary>
    /// Resuelve una ruta relativa o absoluta garantizando que no escape del directorio raíz del repositorio.
    /// </summary>
    public string ResolveSafePath(string relativeOrFullPath)
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

    /// <summary>
    /// Devuelve la ruta relativa al repoRoot normalizada con slashes tipo Unix.
    /// </summary>
    public string GetRelativePath(string fullPath)
    {
        return Path.GetRelativePath(_repoRoot, fullPath).Replace('\\', '/');
    }

    /// <summary>
    /// Determina si una ruta pertenece a un directorio temporal, de build o ignorado.
    /// </summary>
    public static bool IsIgnoredPath(string fullPath)
    {
        var parts = fullPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => IgnoredDirectories.Contains(p) || (p.StartsWith(".") && p != "." && p != ".."));
    }
}
