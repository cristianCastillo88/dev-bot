using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Subagents;
using DevBot.Cli.Tools;

namespace DevBot.Cli.Core;

/// <summary>
/// Fábrica concreta que ensambla instancias de <see cref="IOrchestrator"/>
/// inyectando subagentes y servicios resueltos desde el contenedor IoC.
/// </summary>
public class OrchestratorFactory(
    IScoutAgent scoutAgent,
    IPlannerAgent plannerAgent,
    ICoderAgent coderAgent,
    IReviewerAgent reviewerAgent,
    IProjectStackDetector stackDetector) : IOrchestratorFactory
{
    private readonly IScoutAgent _scoutAgent = scoutAgent ?? throw new ArgumentNullException(nameof(scoutAgent));
    private readonly IPlannerAgent _plannerAgent = plannerAgent ?? throw new ArgumentNullException(nameof(plannerAgent));
    private readonly ICoderAgent _coderAgent = coderAgent ?? throw new ArgumentNullException(nameof(coderAgent));
    private readonly IReviewerAgent _reviewerAgent = reviewerAgent ?? throw new ArgumentNullException(nameof(reviewerAgent));
    private readonly IProjectStackDetector _stackDetector = stackDetector ?? throw new ArgumentNullException(nameof(stackDetector));

    /// <inheritdoc />
    public IOrchestrator Create(AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Orchestrator(
            context,
            _scoutAgent,
            _plannerAgent,
            _coderAgent,
            _reviewerAgent,
            _stackDetector,
            new GitTools(context.RepoRoot)
        );
    }
}
