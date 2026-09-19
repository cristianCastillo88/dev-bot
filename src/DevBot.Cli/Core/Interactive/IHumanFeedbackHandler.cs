namespace DevBot.Cli.Core.Interactive;

public interface IHumanFeedbackHandler
{
    Task<HumanDecision> RequestScoutApprovalAsync(
        ScoutPlanSummary plan,
        CancellationToken cancellationToken = default);
}
