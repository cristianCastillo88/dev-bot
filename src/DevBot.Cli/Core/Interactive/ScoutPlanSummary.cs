using DevBot.Cli.Core.Modes;
using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core.Interactive;

/// <summary>
/// Resumen técnico presentado al desarrollador para su auditoría y aprobación en la fase Human-in-the-Loop.
/// </summary>
/// <param name="TaskDescription">Requerimiento solicitado originalmente por el usuario.</param>
/// <param name="Mode">Modo de operación agéntico activo.</param>
/// <param name="Stack">Información del stack tecnológico detectado.</param>
/// <param name="TargetFiles">Lista de archivos prioritarios que el agente propone modificar.</param>
/// <param name="StrategySummary">Descripción de la estrategia de compilación y pruebas asociada.</param>
/// <param name="ScoutReportSnippet">Fragmento sintético del reporte del ScoutAgent.</param>
public sealed record ScoutPlanSummary(
    string TaskDescription,
    AgentMode Mode,
    ProjectStackInfo Stack,
    IReadOnlyList<string> TargetFiles,
    string StrategySummary,
    string ScoutReportSnippet
);
