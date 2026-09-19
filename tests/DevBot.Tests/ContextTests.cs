using DevBot.Cli.Core;
using Xunit;

namespace DevBot.Tests;

public class ContextTests : IDisposable
{
    private readonly string _testDir;

    public ContextTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "devbot_context_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Fact]
    public void AgentContext_ArtifactPersistence_WorksCleanlyOnDisk()
    {
        var context = new AgentContext(_testDir, "test-api-key", "gemini-2.5-flash");

        // 1. Task persistence
        context.SaveTask("Implement user registration");
        Assert.True(File.Exists(context.TaskFilePath));
        Assert.Equal("Implement user registration", context.ReadTask());

        // 2. Scout report persistence
        context.SaveScoutReport("# Scout Report\nTarget: UserController.cs");
        Assert.True(File.Exists(context.ScoutReportPath));
        Assert.Contains("UserController.cs", context.ReadScoutReport());

        // 3. Review results persistence
        context.SaveReviewResults("ALL_PASS");
        Assert.True(File.Exists(context.ReviewResultsPath));
        Assert.Equal("ALL_PASS", context.ReadReviewResults());
    }

    [Fact]
    public void ExecutionMetrics_TracksTimingAndRetries()
    {
        var metrics = new ExecutionMetrics();
        metrics.Start();
        Thread.Sleep(50);
        metrics.Stop();

        Assert.True(metrics.Elapsed.TotalMilliseconds >= 40);
        metrics.CoderRetries = 2;
        metrics.TokensConsumed = 4500;
        metrics.Success = true;
        metrics.CommitHash = "a1b2c3d";

        Assert.Equal(2, metrics.CoderRetries);
        Assert.Equal(4500, metrics.TokensConsumed);
        Assert.True(metrics.Success);
        Assert.Equal("a1b2c3d", metrics.CommitHash);
    }

    [Fact]
    public void ExecutionMetrics_TracksPhasesAndTokensAccurately()
    {
        var metrics = new ExecutionMetrics();
        metrics.Start();

        // Phase 1: Scout
        metrics.StartPhase("Scout Agent");
        metrics.RecordToolCall("ListFiles");
        metrics.RecordToolCall("SearchCode");
        metrics.RecordToolCall("SearchCode");
        metrics.RecordTokens(500, 150);
        metrics.EndPhase(true, "Scout finished");

        // Phase 2: Coder
        metrics.StartPhase("Coder Agent (Iter #1)");
        metrics.RecordToolCall("ApplyDiff");
        metrics.RecordTokens(1000, 300);
        metrics.EndPhase(true);

        metrics.Stop();

        Assert.Equal(2, metrics.Phases.Count);
        Assert.Equal(1950, metrics.TokensConsumed);

        var scoutPhase = metrics.Phases[0];
        Assert.Equal("Scout Agent", scoutPhase.PhaseName);
        Assert.Equal(650, scoutPhase.TotalTokens);
        Assert.Equal(1, scoutPhase.ToolCalls["ListFiles"]);
        Assert.Equal(2, scoutPhase.ToolCalls["SearchCode"]);
        Assert.True(scoutPhase.Success);

        var coderPhase = metrics.Phases[1];
        Assert.Equal(1300, coderPhase.TotalTokens);
        Assert.Equal(1, coderPhase.ToolCalls["ApplyDiff"]);
    }

    [Fact]
    public void AgentLogger_LogsEventsAndExportsJson_Successfully()
    {
        var context = new AgentContext(_testDir, "test-api-key");
        var logger = context.Logger;

        logger.LogEvent("ScoutAgent", "ToolCall", "ReadFile on Program.cs");
        logger.LogEvent("CoderAgent", "ToolResult", "Applied diff to Program.cs");

        Assert.Equal(2, logger.GetEntries().Count);
        Assert.True(File.Exists(logger.LatestLogPath));

        string logContent = File.ReadAllText(logger.LatestLogPath);
        Assert.Contains("ReadFile on Program.cs", logContent);
        Assert.Contains("Applied diff to Program.cs", logContent);

        logger.SaveJson();
        Assert.True(File.Exists(logger.LatestJsonPath));
        string jsonContent = File.ReadAllText(logger.LatestJsonPath);
        Assert.Contains("ScoutAgent", jsonContent);
        Assert.Contains("ToolCall", jsonContent);
    }

    [Fact]
    public void RunReportGenerator_GeneratesMarkdownReportWithMetricsAndLogs()
    {
        var context = new AgentContext(_testDir, "test-api-key")
        {
            TaskDescription = "Add health check endpoint",
            TargetBranch = "feature/health-check",
            OriginalBranch = "main"
        };

        context.ModifiedFiles.Add("Controllers/HealthController.cs");
        context.SaveScoutReport("Architectural analysis shows Web API pattern.");
        context.SaveReviewResults("ALL_PASS - All 5 tests passed cleanly.");

        context.Metrics.Start();
        context.Metrics.StartPhase("Scout Agent");
        context.Metrics.RecordToolCall("ListFiles");
        context.Metrics.RecordTokens(300, 100);
        context.Metrics.EndPhase(true);

        context.Metrics.StartPhase("Coder Agent (Iter #1)");
        context.Metrics.RecordToolCall("WriteFile");
        context.Metrics.RecordTokens(800, 200);
        context.Metrics.EndPhase(true);

        context.Metrics.CommitHash = "c7d8e9f";
        context.Metrics.Success = true;
        context.Metrics.Stop();

        string report = RunReportGenerator.GenerateReport(context, "Controllers/HealthController.cs | 25 +++++");

        Assert.Contains("DevBot - Informe Ejecutivo de Ejecución", report);
        Assert.Contains("Add health check endpoint", report);
        Assert.Contains("Scout Agent", report);
        Assert.Contains("Controllers/HealthController.cs", report);
        Assert.Contains("c7d8e9f", report);
        Assert.Contains("ALL_PASS", report);

        Assert.True(File.Exists(context.LatestReportPath));
    }
}
