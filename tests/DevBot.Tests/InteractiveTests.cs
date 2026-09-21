using DevBot.Cli.Core;
using DevBot.Cli.Core.Interactive;
using DevBot.Cli.Core.Modes;
using DevBot.Cli.Core.Strategies;
using Xunit;

namespace DevBot.Tests;

public class InteractiveTests
{
    [Fact]
    public void ScoutPlanExtractor_ExtractsTargetFiles_Correctly()
    {
        string sampleReport = """
            # Scout Technical Report
            ## 1. Problem & Context Analysis
            Analyzing user authentication issue.

            ## 2. Exact Target Files & Locations
            - `src/Controllers/AuthController.cs`: Add login method
            - `src/Services/AuthService.cs`: Inject token generator
            - src/Models/User.cs

            ## 3. Reference File & Coding Conventions
            Follow UserController.cs
            """;

        var targetFiles = ScoutPlanExtractor.ExtractTargetFiles(sampleReport);

        Assert.Equal(3, targetFiles.Count);
        Assert.Contains("src/Controllers/AuthController.cs", targetFiles);
        Assert.Contains("src/Services/AuthService.cs", targetFiles);
        Assert.Contains("src/Models/User.cs", targetFiles);
    }

    [Fact]
    public void ScoutPlanExtractor_ExtractSummary_BuildsCleanPlanSummary()
    {
        string sampleReport = """
            # Scout Technical Report
            ## 2. Target Files
            - `Service.cs`
            Summary snippet text here.
            """;

        var stack = new ProjectStackInfo(ProjectStackType.DotNet, "C#", "dotnet", "App.sln", Array.Empty<string>(), Array.Empty<string>());
        var strategy = new DotNetStrategy();

        var summary = ScoutPlanExtractor.ExtractSummary(
            "Fix payment gateway timeout",
            AgentMode.Bug,
            stack,
            strategy,
            sampleReport
        );

        Assert.Equal("Fix payment gateway timeout", summary.TaskDescription);
        Assert.Equal(AgentMode.Bug, summary.Mode);
        Assert.Single(summary.TargetFiles);
        Assert.Equal("Service.cs", summary.TargetFiles[0]);
        Assert.Contains("C# / .NET Strategy", summary.StrategySummary);
    }

    [Fact]
    public async Task MockFeedbackHandler_CanSimulateHumanDecisions()
    {
        var plan = new ScoutPlanSummary(
            "Task 1",
            AgentMode.Feature,
            new ProjectStackInfo(ProjectStackType.NodeJs, "TypeScript", "npm", null, Array.Empty<string>(), Array.Empty<string>()),
            new[] { "index.ts" },
            "Node Strategy",
            "Report"
        );

        var approvedHandler = new MockFeedbackHandler(HumanDecisionType.Approved);
        var decision1 = await approvedHandler.RequestScoutApprovalAsync(plan);
        Assert.Equal(HumanDecisionType.Approved, decision1.Type);

        var clarifiedHandler = new MockFeedbackHandler(HumanDecisionType.Clarified, "Ensure to use async/await exclusively");
        var decision2 = await clarifiedHandler.RequestScoutApprovalAsync(plan);
        Assert.Equal(HumanDecisionType.Clarified, decision2.Type);
        Assert.Equal("Ensure to use async/await exclusively", decision2.AdditionalGuidance);

        var abortedHandler = new MockFeedbackHandler(HumanDecisionType.Aborted);
        var decision3 = await abortedHandler.RequestScoutApprovalAsync(plan);
        Assert.Equal(HumanDecisionType.Aborted, decision3.Type);
    }

    [Fact]
    public async Task MockFeedbackHandler_CanSimulatePlanApprovalDecisions()
    {
        var planDef = new DevBot.Cli.Core.Planning.PlanDefinition
        {
            TaskDescription = "Implement login flow",
            ArchitecturalSummary = "Auth architecture",
            Milestones = new List<DevBot.Cli.Core.Planning.PlanMilestone>
            {
                new()
                {
                    Order = 1,
                    Id = "m1",
                    Title = "Create user model",
                    Scope = "domain",
                    TargetFiles = new List<string> { "User.cs" },
                    VerificationCommand = "dotnet test"
                }
            }
        };

        var planSummary = new PlanApprovalSummary(
            "Implement login flow",
            AgentMode.Feature,
            new ProjectStackInfo(ProjectStackType.DotNet, "C#", "dotnet", "App.sln", Array.Empty<string>(), Array.Empty<string>()),
            "C# / .NET Strategy",
            planDef,
            new[] { "User.cs" },
            "Auth architecture"
        );

        var approvedHandler = new MockFeedbackHandler(HumanDecisionType.Approved);
        var decision1 = await approvedHandler.RequestPlanApprovalAsync(planSummary);
        Assert.Equal(HumanDecisionType.Approved, decision1.Type);

        var clarifiedHandler = new MockFeedbackHandler(HumanDecisionType.Clarified, "Use BCrypt for password hashing");
        var decision2 = await clarifiedHandler.RequestPlanApprovalAsync(planSummary);
        Assert.Equal(HumanDecisionType.Clarified, decision2.Type);
        Assert.Equal("Use BCrypt for password hashing", decision2.AdditionalGuidance);

        var abortedHandler = new MockFeedbackHandler(HumanDecisionType.Aborted);
        var decision3 = await abortedHandler.RequestPlanApprovalAsync(planSummary);
        Assert.Equal(HumanDecisionType.Aborted, decision3.Type);
    }

    [Fact]
    public void AgentContext_AutoApprove_SetsProperly()
    {
        var context = new AgentContext(Path.GetTempPath(), "dummy-key");
        Assert.False(context.AutoApprove);

        context.AutoApprove = true;
        Assert.True(context.AutoApprove);

        context.AdditionalUserGuidance = "Use Redis cache";
        Assert.Equal("Use Redis cache", context.AdditionalUserGuidance);
    }

    private class MockFeedbackHandler : IHumanFeedbackHandler
    {
        private readonly HumanDecisionType _type;
        private readonly string? _guidance;

        public MockFeedbackHandler(HumanDecisionType type, string? guidance = null)
        {
            _type = type;
            _guidance = guidance;
        }

        public Task<HumanDecision> RequestPlanApprovalAsync(PlanApprovalSummary plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HumanDecision(_type, _guidance));
        }

        public Task<HumanDecision> RequestScoutApprovalAsync(ScoutPlanSummary plan, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HumanDecision(_type, _guidance));
        }
    }
}
