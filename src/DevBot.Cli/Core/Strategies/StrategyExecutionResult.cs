namespace DevBot.Cli.Core.Strategies;

/// <summary>
/// Modela el resultado de la invocación de una acción dentro de una estrategia (build, test, lint).
/// </summary>
/// <param name="Success">Indica si el proceso concluyó con código de salida exitoso (usualmente 0).</param>
/// <param name="ExitCode">Código de salida retornado por el proceso del sistema operativo.</param>
/// <param name="Output">Salida estándar capturada (stdout).</param>
/// <param name="Errors">Salida de error capturada (stderr).</param>
/// <param name="CommandExecuted">Comando exacto ejecutado en la terminal.</param>
public sealed record StrategyExecutionResult(
    bool Success,
    int ExitCode,
    string Output,
    string Errors,
    string CommandExecuted
);
