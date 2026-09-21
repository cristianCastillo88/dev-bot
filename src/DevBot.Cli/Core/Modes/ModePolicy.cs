namespace DevBot.Cli.Core.Modes;

/// <summary>
/// Define las directrices operacionales, restricciones de inmutabilidad y límites de reintentos
/// aplicados a los subagentes según la naturaleza de la misión (Feature, Bug, Refactor, Test).
/// </summary>
/// <param name="Mode">Modo de operación agéntico.</param>
/// <param name="DisplayName">Nombre legible para la interfaz de consola.</param>
/// <param name="DefaultMaxRetries">Máximo de reintentos en el bucle Coder-Reviewer.</param>
/// <param name="EnforceContractImmutability">Indica si se prohíbe alterar firmas públicas y contratos de API.</param>
/// <param name="RequireFailingTestFirst">Indica si el subagente Coder debe crear o actualizar una prueba que falle antes del arreglo.</param>
/// <param name="ScoutInstructions">Directrices especializadas para la fase de análisis y exploración.</param>
/// <param name="CoderInstructions">Directrices especializadas para la fase de implementación quirúrgica.</param>
/// <param name="ReviewerInstructions">Directrices especializadas para la fase de pruebas y verificación.</param>
public record ModePolicy(
    AgentMode Mode,
    string DisplayName,
    int DefaultMaxRetries,
    bool EnforceContractImmutability,
    bool RequireFailingTestFirst,
    string ScoutInstructions,
    string CoderInstructions,
    string ReviewerInstructions
);
