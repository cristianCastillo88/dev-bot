using DevBot.Cli.Core.Planning;

namespace DevBot.Cli.Core;

/// <summary>
/// Servicio que inspecciona y gestiona el estado de reanudación de ejecuciones interrumpidas.
/// </summary>
public static class ResumableExecutionService
{
    /// <summary>
    /// Comprueba si existe un plan ejecutable previamente persistido en .agent/plan.json.
    /// </summary>
    /// <param name="repoRoot">Directorio raíz del repositorio.</param>
    /// <returns>Verdadero si existe un plan válido con hitos; de lo contrario, falso.</returns>
    public static bool HasPendingPlan(string repoRoot)
    {
        string planPath = Path.Combine(repoRoot, ".agent", "plan.json");
        if (!File.Exists(planPath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(planPath);
            var plan = PlanDefinition.FromJson(json);
            return plan != null && plan.Milestones.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Carga el plan previamente almacenado si está disponible.
    /// </summary>
    /// <param name="repoRoot">Directorio raíz del repositorio.</param>
    /// <returns>Instancia de <see cref="PlanDefinition"/> o null si no se puede leer.</returns>
    public static PlanDefinition? LoadPlan(string repoRoot)
    {
        string planPath = Path.Combine(repoRoot, ".agent", "plan.json");
        if (!File.Exists(planPath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(planPath);
            return PlanDefinition.FromJson(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Calcula el índice del primer hito pendiente de ejecución (0-indexado).
    /// </summary>
    /// <param name="plan">Plan de ejecución descompuesto.</param>
    /// <returns>Índice del primer hito incompleto, o -1 si todos están finalizados.</returns>
    public static int GetResumeStartIndex(PlanDefinition plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Milestones.FindIndex(m => !m.IsCompleted || string.IsNullOrEmpty(m.CheckpointCommitHash));
    }
}
