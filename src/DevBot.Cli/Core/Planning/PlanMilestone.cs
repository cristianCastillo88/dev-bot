namespace DevBot.Cli.Core.Planning;

public class PlanMilestone
{
    public int Order { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Scope { get; set; } = "core";
    public string Description { get; set; } = string.Empty;
    public List<string> TargetFiles { get; set; } = new();
    public string VerificationCommand { get; set; } = string.Empty;
    public List<string> RequiredSkills { get; set; } = new();
    public bool IsCompleted { get; set; }
    public string? CheckpointCommitHash { get; set; }
}
