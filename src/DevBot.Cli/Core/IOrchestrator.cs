namespace DevBot.Cli.Core;

/// <summary>
/// Contrato para el orquestador del ciclo de vida del agente autónomo.
/// </summary>
public interface IOrchestrator
{
    /// <summary>
    /// Ejecuta el ciclo agéntico completo para la tarea especificada.
    /// </summary>
    /// <param name="taskDescription">Descripción del requerimiento o corrección a ejecutar.</param>
    /// <param name="cancellationToken">Token de cancelación cooperativa.</param>
    /// <returns>Verdadero si la misión finalizó exitosamente; de lo contrario, falso.</returns>
    Task<bool> RunAsync(string taskDescription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reanuda la ejecución de un plan existente desde el primer hito incompleto.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación cooperativa.</param>
    /// <returns>Verdadero si la reanudación finalizó exitosamente; de lo contrario, falso.</returns>
    Task<bool> ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta únicamente las fases de exploración y planificación sin realizar modificaciones de código ni commits.
    /// </summary>
    /// <param name="taskDescription">Descripción del requerimiento a planificar.</param>
    /// <param name="cancellationToken">Token de cancelación cooperativa.</param>
    /// <returns>Verdadero si el plan fue generado y aprobado; de lo contrario, falso.</returns>
    Task<bool> PlanOnlyAsync(string taskDescription, CancellationToken cancellationToken = default);
}
