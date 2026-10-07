using System.Text.RegularExpressions;

namespace DevBot.Cli.Core;

/// <summary>
/// Utilidad de seguridad para sanitizar y validar entradas antes de su ejecución en el sistema o en Git.
/// Previene vulnerabilidades de Command Injection y manipulación de parámetros.
/// </summary>
public static partial class InputSanitizer
{
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9/_.\-]*$")]
    private static partial Regex SafeBranchRegex();

    private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", "npm", "npx", "yarn", "pnpm", "mvn", "gradle", "pytest", "python", "python3",
        "flake8", "git", "tsc", "pip", "pip3", "go", "cargo", "rustc", "node"
    };

    /// <summary>
    /// Valida que el nombre de rama Git contenga únicamente caracteres seguros.
    /// Lanza <see cref="ArgumentException"/> si el nombre contiene secuencias o caracteres peligrosos.
    /// </summary>
    /// <param name="branchName">Nombre de la rama a validar.</param>
    /// <returns>El mismo nombre validado si es seguro.</returns>
    public static string ValidateBranchName(string branchName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        string trimmed = branchName.Trim();

        if (trimmed.Contains("..") ||
            trimmed.Contains("//") ||
            trimmed.Contains(" ") ||
            trimmed.Contains(';') ||
            trimmed.Contains('&') ||
            trimmed.Contains('|') ||
            trimmed.Contains('`') ||
            trimmed.Contains('$') ||
            trimmed.Contains('<') ||
            trimmed.Contains('>') ||
            trimmed.Contains('"') ||
            trimmed.Contains('\'') ||
            trimmed.Contains('\\') ||
            !SafeBranchRegex().IsMatch(trimmed))
        {
            throw new ArgumentException(
                $"Invalid or unsafe branch name '{branchName}'. Branch names must be alphanumeric and may only contain '/', '-', '_', or '.' without dangerous shell characters.",
                nameof(branchName));
        }

        return trimmed;
    }

    /// <summary>
    /// Verifica si un comando se encuentra dentro de la lista blanca de herramientas de compilación y prueba permitidas.
    /// Lanza <see cref="UnauthorizedAccessException"/> si el comando no está autorizado.
    /// </summary>
    /// <param name="command">Nombre del ejecutable o comando a validar.</param>
    /// <returns>El mismo comando validado.</returns>
    public static string ValidateCommand(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        string rawName = Path.GetFileNameWithoutExtension(command.Trim());

        if (!AllowedCommands.Contains(rawName))
        {
            throw new UnauthorizedAccessException(
                $"Execution blocked: Command '{command}' is not in the allowed execution whitelist. Allowed commands: [{string.Join(", ", AllowedCommands)}]");
        }

        return command.Trim();
    }

    /// <summary>
    /// Verifica de forma no-excepcional si un nombre de rama es seguro.
    /// </summary>
    public static bool IsSafeBranchName(string? branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName)) return false;
        try
        {
            ValidateBranchName(branchName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifica de forma no-excepcional si un comando está en la lista blanca permitida.
    /// </summary>
    public static bool IsAllowedCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        try
        {
            ValidateCommand(command);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
