namespace DevBot.Cli.Core.Interactive;

/// <summary>
/// Dictamen emitido por el desarrollador en el punto de control Human-in-the-Loop tras revisar el plan del ScoutAgent.
/// </summary>
public enum HumanDecisionType
{
    /// <summary>Plan aprobado; se autoriza la codificación quirúrgica.</summary>
    Approved,

    /// <summary>Plan enriquecido con directivas o instrucciones aclaratorias adicionales del usuario.</summary>
    Clarified,

    /// <summary>Misión cancelada por el usuario; se aborta y se limpian ramas temporales.</summary>
    Aborted
}
