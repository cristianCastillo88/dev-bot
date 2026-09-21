using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core.Auditing;

/// <summary>
/// Resultado consolidado de una auditoría de seguridad y calidad pre-commit.
/// </summary>
/// <param name="Passed">Indica si la auditoría general fue aprobada sin bloqueos de seguridad.</param>
/// <param name="SecurityFindings">Colección de credenciales o secretos detectados en el código modificado.</param>
/// <param name="LinterResult">Resultado de la ejecución del formateador o linter de código.</param>
/// <param name="Summary">Resumen textual descriptivo del dictamen de la auditoría.</param>
public sealed record AuditResult(
    bool Passed,
    IReadOnlyList<SecurityFinding> SecurityFindings,
    StrategyExecutionResult? LinterResult,
    string Summary
);
