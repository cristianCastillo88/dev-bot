using DevBot.Cli.Core;
using DevBot.Cli.Core.Planning;
using Xunit;

namespace DevBot.Tests;

public class RenderingTests : IDisposable
{
    private readonly string _testDir;

    public RenderingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "devbot_render_test_" + Guid.NewGuid().ToString("N"));
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
    public void ConsoleReportRenderer_PrintStartupBanner_DoesNotThrow()
    {
        var context = new AgentContext(_testDir, "test-api-key");
        var ex = Record.Exception(() => ConsoleReportRenderer.PrintStartupBanner(context, "Feature requirement description"));
        Assert.Null(ex);
    }

    [Fact]
    public void ConsoleReportRenderer_PrintMilestoneHeader_DoesNotThrow()
    {
        var milestone = new PlanMilestone
        {
            Order = 1,
            Title = "Infraestructura",
            Scope = "Core",
            Description = "Configurar base de datos",
            TargetFiles = new List<string> { "DbConfig.cs" },
            VerificationCommand = "dotnet build"
        };

        var ex = Record.Exception(() => ConsoleReportRenderer.PrintMilestoneHeader(milestone, 3));
        Assert.Null(ex);
    }

    [Fact]
    public void ConsoleReportRenderer_PrintSuccessSummary_DoesNotThrow()
    {
        var context = new AgentContext(_testDir, "test-api-key")
        {
            TargetBranch = "feature/test",
            PullRequestUrl = "https://github.com/org/repo/pull/1"
        };
        context.Metrics.Start();
        context.Metrics.CommitHash = "abcdef1";
        context.Metrics.Stop();

        var ex = Record.Exception(() => ConsoleReportRenderer.PrintSuccessSummary(context, "file.cs | 10 ++++"));
        Assert.Null(ex);
    }

    [Fact]
    public void ConsoleReportRenderer_PrintFailureSummary_DoesNotThrow()
    {
        var context = new AgentContext(_testDir, "test-api-key");
        context.Metrics.Start();
        context.Metrics.ErrorMessage = "Compilation error";
        context.Metrics.Stop();

        var ex = Record.Exception(() => ConsoleReportRenderer.PrintFailureSummary(context));
        Assert.Null(ex);
    }

    [Fact]
    public void ConsoleReportRenderer_ThrowsOnNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ConsoleReportRenderer.PrintStartupBanner(null!, "task"));
        Assert.Throws<ArgumentNullException>(() => ConsoleReportRenderer.PrintMilestoneHeader(null!, 1));
        Assert.Throws<ArgumentNullException>(() => ConsoleReportRenderer.PrintSuccessSummary(null!));
        Assert.Throws<ArgumentNullException>(() => ConsoleReportRenderer.PrintFailureSummary(null!));
    }
}
