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
}
