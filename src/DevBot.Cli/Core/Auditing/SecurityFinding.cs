namespace DevBot.Cli.Core.Auditing;

/// <summary>
/// Modela un hallazgo de vulnerabilidad o credencial privada detectada por el escáner de seguridad en el código modificado.
/// </summary>
/// <param name="FilePath">Ruta relativa del archivo donde se localizó el hallazgo.</param>
/// <param name="LineNumber">Número de línea (1-indexed) de la ocurrencia.</param>
/// <param name="RuleId">Identificador de la regla de detección activada (ej. "SEC001-AWS-KEY").</param>
/// <param name="Description">Descripción del tipo de secreto identificado.</param>
/// <param name="RedactedSnippet">Fragmento de código con la credencial enmascarada para evitar exponer el secreto en reportes.</param>
public sealed record SecurityFinding(
    string FilePath,
    int LineNumber,
    string RuleId,
    string Description,
    string RedactedSnippet
);
