using DevBot.Cli.Core.Modes;
using DevBot.Cli.Core.Planning;
using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core.Interactive;

/// <summary>
/// Resumen integral del plan de ejecución (diagnóstico y descomposición en hitos) 
/// presentado al desarrollador para su auditoría y aprobación en la fase Human-in-the-Loop.
/// </summary>
/// <param name="TaskDescription">Requerimiento solicitado originalmente por el usuario.</param>
/// <param name="Mode">Modo de operación agéntico activo.</param>
/// <param name="Stack">Información del stack tecnológico detectado.</param>
/// <param name="StrategySummary">Descripción de la estrategia de compilación y pruebas asociada.</param>
/// <param name="Plan">Definición estructurada del plan con hitos atómicos y comandos de verificación.</param>
/// <param name="TargetFiles">Lista consolidada de archivos prioritarios que serán modificados a lo largo de los hitos.</param>
/// <param name="ArchitecturalSummary">Resumen arquitectónico o diagnóstico relevante del plan.</param>
public sealed record PlanApprovalSummary(
    string TaskDescription,
    AgentMode Mode,
    ProjectStackInfo Stack,
    string StrategySummary,
    PlanDefinition Plan,
    IReadOnlyList<string> TargetFiles,
    string ArchitecturalSummary
);
