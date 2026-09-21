using DevBot.Cli.Core;
using DevBot.Cli.Core.Auditing;
using DevBot.Cli.Core.Configuration;
using DevBot.Cli.Core.DependencyInjection;
using DevBot.Cli.Core.Memory;
using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Subagents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevBot.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void ServiceCollectionExtensions_RegistersAllCoreServices()
    {
        var services = new ServiceCollection();
        services.AddDevBotCore();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IProjectStackDetector>());
        Assert.NotNull(provider.GetService<IRepositoryMemoryService>());
        Assert.NotNull(provider.GetService<IPreCommitAuditor>());
        Assert.NotNull(provider.GetService<ICredentialStorageService>());

        Assert.NotNull(provider.GetService<ScoutAgent>());
        Assert.NotNull(provider.GetService<PlannerAgent>());
        Assert.NotNull(provider.GetService<CoderAgent>());
        Assert.NotNull(provider.GetService<ReviewerAgent>());

        Assert.NotNull(provider.GetService<IScoutAgent>());
        Assert.NotNull(provider.GetService<IPlannerAgent>());
        Assert.NotNull(provider.GetService<ICoderAgent>());
        Assert.NotNull(provider.GetService<IReviewerAgent>());
        Assert.NotNull(provider.GetService<IOrchestratorFactory>());
    }

    [Fact]
    public void OrchestratorFactory_CreatesOrchestratorWithContext()
    {
        var services = new ServiceCollection();
        services.AddDevBotCore();

        using var provider = services.BuildServiceProvider();

        string tempDir = Path.GetTempPath();
        var context = new AgentContext(tempDir, "test-api-key");

        var factory = provider.GetRequiredService<IOrchestratorFactory>();
        var orchestrator = factory.Create(context);

        Assert.NotNull(orchestrator);
        Assert.IsAssignableFrom<IOrchestrator>(orchestrator);
    }

    [Fact]
    public void ActivatorUtilities_InstantiatesOrchestratorWithInjectedDependencies()
    {
        var services = new ServiceCollection();
        services.AddDevBotCore();

        using var provider = services.BuildServiceProvider();

        string tempDir = Path.GetTempPath();
        var context = new AgentContext(tempDir, "test-api-key");

        var orchestrator = ActivatorUtilities.CreateInstance<Orchestrator>(provider, context);

        Assert.NotNull(orchestrator);
    }

    [Fact]
    public void Orchestrator_ThrowsArgumentNullException_WhenContextIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new Orchestrator(
            null!,
            new ScoutAgent(),
            new PlannerAgent(),
            new CoderAgent(),
            new ReviewerAgent()
        ));
    }

    [Fact]
    public void Orchestrator_BackwardCompatibilityConstructor_InstantiatesSuccessfully()
    {
        string tempDir = Path.GetTempPath();
        var context = new AgentContext(tempDir, "test-api-key");

        var orchestrator = new Orchestrator(context);

        Assert.NotNull(orchestrator);
    }
}
