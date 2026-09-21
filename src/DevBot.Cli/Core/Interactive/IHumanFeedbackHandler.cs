namespace DevBot.Cli.Core.Interactive;

/// <summary>
/// Contrato abstracto para la interacción y aprobación humana (Human-in-the-Loop).
/// </summary>
public interface IHumanFeedbackHandler
{
    /// <summary>
    /// Solicita al desarrollador la revisión y aprobación del plan de ejecución estructurado en hitos.
    /// </summary>
    /// <param name="plan">Resumen integral del plan con hitos y comandos de verificación.</param>
    /// <param name="cancellationToken">Token de cancelación cooperativa.</param>
    /// <returns>Decisión tomada por el desarrollador (aprobado, con aclaraciones o abortado).</returns>
    Task<HumanDecision> RequestPlanApprovalAsync(
        PlanApprovalSummary plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Método de compatibilidad para revisión preliminar del diagnóstico Scout.
    /// </summary>
    /// <param name="plan">Resumen técnico preliminar del diagnóstico Scout.</param>
    /// <param name="cancellationToken">Token de cancelación cooperativa.</param>
    /// <returns>Decisión tomada por el desarrollador.</returns>
    Task<HumanDecision> RequestScoutApprovalAsync(
        ScoutPlanSummary plan,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new HumanDecision(HumanDecisionType.Approved));
    }
}
