using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core.Validation;

/// <summary>
/// Contrato unificado para validación estática de sintaxis de código antes de persistir cambios.
/// Centraliza la verificación para evitar duplicación y bloqueo de threads (DRY / SRP).
/// </summary>
public interface ISyntaxValidator
{
    /// <summary>
    /// Valida sincrónicamente la sintaxis del contenido en memoria según la extensión del archivo.
    /// </summary>
    SyntaxValidationResult Validate(string filePath, string content);

    /// <summary>
    /// Valida asincrónicamente la sintaxis del contenido en memoria.
    /// </summary>
    Task<SyntaxValidationResult> ValidateAsync(string filePath, string content, CancellationToken ct = default);
}
