namespace DevBot.Cli.Core.Interactive;

/// <summary>
/// Encapsula la decisión humana tomada en el punto de control interactivo con el desarrollador.
/// </summary>
/// <param name="Type">Tipo de decisión tomada (Approved, Clarified, Aborted).</param>
/// <param name="AdditionalGuidance">Texto opcional con aclaraciones o pautas adicionales para el CoderAgent.</param>
public sealed record HumanDecision(
    HumanDecisionType Type,
    string? AdditionalGuidance = null
);
