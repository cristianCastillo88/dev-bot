using DevBot.Cli.Core.Auditing;
using DevBot.Cli.Core.Configuration;
using DevBot.Cli.Core.Memory;
using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Subagents;
using Microsoft.Extensions.DependencyInjection;

namespace DevBot.Cli.Core.DependencyInjection;

/// <summary>
/// Métodos de extensión para registrar los servicios y subagentes de DevBot en el contenedor IoC.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra en la colección de servicios todos los componentes esenciales del motor agéntico.
    /// </summary>
    /// <param name="services">Colección de servicios del contenedor.</param>
    /// <returns>La misma colección para encadenamiento fluido.</returns>
    public static IServiceCollection AddDevBotCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Servicios Singleton (stateless o infraestructura reutilizable)
        services.AddSingleton<IProjectStackDetector, ProjectStackDetector>();
        services.AddSingleton<IRepositoryMemoryService, RepositoryMemoryService>();
        services.AddSingleton<IPreCommitAuditor, PreCommitAuditor>();
        services.AddSingleton<ICredentialStorageService, CredentialStorageService>();

        // Subagentes Transient (interfaces y tipos concretos)
        services.AddTransient<IScoutAgent, ScoutAgent>();
        services.AddTransient<IPlannerAgent, PlannerAgent>();
        services.AddTransient<ICoderAgent, CoderAgent>();
        services.AddTransient<IReviewerAgent, ReviewerAgent>();
        services.AddTransient<ScoutAgent>();
        services.AddTransient<PlannerAgent>();
        services.AddTransient<CoderAgent>();
        services.AddTransient<ReviewerAgent>();

        // Orquestador y Fábrica de Orquestación
        services.AddTransient<IOrchestratorFactory, OrchestratorFactory>();
        services.AddTransient<Orchestrator>();

        return services;
    }
}
