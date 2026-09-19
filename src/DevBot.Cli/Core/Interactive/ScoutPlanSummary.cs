using DevBot.Cli.Core.Modes;
using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core.Interactive;

public record ScoutPlanSummary(
    string TaskDescription,
    AgentMode Mode,
    ProjectStackInfo Stack,
    IReadOnlyList<string> TargetFiles,
    string StrategySummary,
    string ScoutReportSnippet
);

public enum HumanDecisionType
{
    Approved,
    Clarified,
    Aborted
}

public record HumanDecision(
    HumanDecisionType Type,
    string? AdditionalGuidance = null
);
