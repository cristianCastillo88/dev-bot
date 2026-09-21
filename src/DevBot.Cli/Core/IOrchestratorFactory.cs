namespace DevBot.Cli.Core;

/// <summary>
/// Fábrica abstracta para la creación desacoplada de instancias de <see cref="IOrchestrator"/>.
/// Permite combinar dependencias del contenedor IoC con parámetros de estado en tiempo de ejecución.
/// </summary>
public interface IOrchestratorFactory
{
    /// <summary>
    /// Crea una nueva instancia de <see cref="IOrchestrator"/> asociada al contexto de ejecución provisto.
    /// </summary>
    /// <param name="context">Contexto de configuración y estado del agente.</param>
    /// <returns>Instancia configurada de <see cref="IOrchestrator"/>.</returns>
    IOrchestrator Create(AgentContext context);
}
