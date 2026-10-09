using DevBot.Cli.Core;
using DevBot.Cli.Core.Configuration;
using DevBot.Cli.Core.DependencyInjection;
using DevBot.Cli.Core.Interactive;
using DevBot.Cli.Core.Planning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevBot.Tests;

public class Phase4FeaturesTests : IDisposable
{
    private readonly string _testDir;

    public Phase4FeaturesTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "devbot_p4_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    #region Tarea 4.1: Resumable Execution (--continue)

    [Fact]
    public void ResumableExecutionService_HasPendingPlan_ReturnsFalse_WhenNoPlan()
    {
        bool hasPlan = ResumableExecutionService.HasPendingPlan(_testDir);
        Assert.False(hasPlan);
    }

    [Fact]
    public void ResumableExecutionService_HasPendingPlan_ReturnsTrue_WhenValidPlanExists()
    {
        string agentDir = Path.Combine(_testDir, ".agent");
        Directory.CreateDirectory(agentDir);

        var plan = new PlanDefinition
        {
            TaskDescription = "Implement login",
            Milestones = new List<PlanMilestone>
            {
                new() { Order = 1, Title = "Create User Model", IsCompleted = true, CheckpointCommitHash = "abc" },
                new() { Order = 2, Title = "Create Auth Controller", IsCompleted = false }
            }
        };

        File.WriteAllText(Path.Combine(agentDir, "plan.json"), plan.ToJson());

        bool hasPlan = ResumableExecutionService.HasPendingPlan(_testDir);
        Assert.True(hasPlan);

        var loaded = ResumableExecutionService.LoadPlan(_testDir);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Milestones.Count);
    }

    [Fact]
    public void ResumableExecutionService_GetResumeStartIndex_IdentifiesFirstIncompleteMilestone()
    {
        var plan = new PlanDefinition
        {
            Milestones = new List<PlanMilestone>
            {
                new() { Order = 1, Title = "M1", IsCompleted = true, CheckpointCommitHash = "c1" },
                new() { Order = 2, Title = "M2", IsCompleted = true, CheckpointCommitHash = "c2" },
                new() { Order = 3, Title = "M3", IsCompleted = false },
                new() { Order = 4, Title = "M4", IsCompleted = false }
            }
        };

        int startIndex = ResumableExecutionService.GetResumeStartIndex(plan);
        Assert.Equal(2, startIndex); // Index 2 is Milestone #3
    }

    [Fact]
    public void ResumableExecutionService_GetResumeStartIndex_ReturnsMinusOne_WhenAllComplete()
    {
        var plan = new PlanDefinition
        {
            Milestones = new List<PlanMilestone>
            {
                new() { Order = 1, Title = "M1", IsCompleted = true, CheckpointCommitHash = "c1" },
                new() { Order = 2, Title = "M2", IsCompleted = true, CheckpointCommitHash = "c2" }
            }
        };

        int startIndex = ResumableExecutionService.GetResumeStartIndex(plan);
        Assert.Equal(-1, startIndex);
    }

    [Fact]
    public void CliArgumentsParser_ParsesContinueFlag_Correctly()
    {
        var options1 = CliArgumentsParser.Parse(new[] { "--continue" });
        Assert.True(options1.ContinueExecution);

        var options2 = CliArgumentsParser.Parse(new[] { "-c" });
        Assert.True(options2.ContinueExecution);

        var options3 = CliArgumentsParser.Parse(new[] { "-t", "some task" });
        Assert.False(options3.ContinueExecution);
    }

    [Fact]
    public async Task Orchestrator_ResumeAsync_ReturnsFalse_WhenNoPendingPlan()
    {
        var services = new ServiceCollection();
        services.AddDevBotCore();
        using var provider = services.BuildServiceProvider();

        var context = new AgentContext(_testDir, "fake-api-key");
        var factory = provider.GetRequiredService<IOrchestratorFactory>();
        var orchestrator = factory.Create(context);

        bool resumed = await orchestrator.ResumeAsync();
        Assert.False(resumed);
        Assert.Contains("No pending plan found", context.Metrics.ErrorMessage);
    }

    #endregion

    #region Tarea 4.2: Modo Dry-Run (--dry-run)

    [Fact]
    public void CliArgumentsParser_ParsesDryRunFlag_Correctly()
    {
        var options1 = CliArgumentsParser.Parse(new[] { "--dry-run" });
        Assert.True(options1.DryRun);

        var options2 = CliArgumentsParser.Parse(new[] { "-t", "demo task" });
        Assert.False(options2.DryRun);
    }

    [Fact]
    public async Task Orchestrator_PlanOnlyAsync_SavesTaskAndInitializesArtifacts()
    {
        var services = new ServiceCollection();
        services.AddDevBotCore();
        using var provider = services.BuildServiceProvider();

        var context = new AgentContext(_testDir, "fake-api-key");
        var factory = provider.GetRequiredService<IOrchestratorFactory>();
        var orchestrator = factory.Create(context);

        // When executing with fake API key, Scout phase will fail, but task must be persisted
        bool success = await orchestrator.PlanOnlyAsync("Build custom microservice");
        Assert.False(success);

        Assert.True(File.Exists(context.TaskFilePath));
        Assert.Equal("Build custom microservice", File.ReadAllText(context.TaskFilePath));
    }

    #endregion

    #region Tarea 4.3: Soporte Multi-Proveedor LLM (LlmProviderResolver)

    [Fact]
    public void LlmProviderResolver_DetectsOpenAi_FromSkPrefix()
    {
        string provider = LlmProviderResolver.DetectProviderByApiKey("sk-proj-1234567890abcdef");
        Assert.Equal("openai", provider);

        string provider2 = LlmProviderResolver.DetectProviderByApiKey("sk-standard-key-123");
        Assert.Equal("openai", provider2);
    }

    [Fact]
    public void LlmProviderResolver_DetectsAnthropic_FromSkAntPrefix()
    {
        string provider = LlmProviderResolver.DetectProviderByApiKey("sk-ant-api03-1234567890");
        Assert.Equal("anthropic", provider);
    }

    [Fact]
    public void LlmProviderResolver_DefaultsToGemini_WhenUnknownOrEmpty()
    {
        Assert.Equal("gemini", LlmProviderResolver.DetectProviderByApiKey("AIzaSySomeGoogleApiKey"));
        Assert.Equal("gemini", LlmProviderResolver.DetectProviderByApiKey(""));
        Assert.Equal("gemini", LlmProviderResolver.DetectProviderByApiKey("   "));
    }

    [Fact]
    public void LlmProviderResolver_ResolvesOpenAiEndpointAndModel_Correctly()
    {
        var (endpoint, modelId, provider) = LlmProviderResolver.Resolve(
            "openai",
            "fake-key",
            requestedModel: null
        );

        Assert.Equal("https://api.openai.com/v1/", endpoint.ToString());
        Assert.Equal("gpt-4o-mini", modelId);
        Assert.Equal("openai", provider);
    }

    [Fact]
    public void LlmProviderResolver_ResolvesAnthropicEndpointAndModel_Correctly()
    {
        var (endpoint, modelId, provider) = LlmProviderResolver.Resolve(
            "anthropic",
            "fake-key",
            requestedModel: null
        );

        Assert.Equal("https://api.anthropic.com/v1/", endpoint.ToString());
        Assert.Equal("claude-3-5-sonnet-20241022", modelId);
        Assert.Equal("anthropic", provider);
    }

    [Fact]
    public void LlmProviderResolver_ResolvesGeminiEndpointAndModel_Correctly()
    {
        var (endpoint, modelId, provider) = LlmProviderResolver.Resolve(
            null,
            "AIzaSyDemoKey",
            requestedModel: null
        );

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/", endpoint.ToString());
        Assert.Equal("gemini-3.5-flash-lite", modelId);
        Assert.Equal("gemini", provider);
    }

    [Fact]
    public void CliArgumentsParser_ParsesProviderFlag_Correctly()
    {
        var options1 = CliArgumentsParser.Parse(new[] { "--provider", "openai" });
        Assert.Equal("openai", options1.Provider);

        var options2 = CliArgumentsParser.Parse(new[] { "-p", "anthropic" });
        Assert.Equal("anthropic", options2.Provider);
    }

    #endregion

    #region Tarea 4.4: Configuración Global ~/.devbot/config.json

    [Fact]
    public void GlobalConfigService_LoadsDefaultConfig_WhenFileDoesNotExist()
    {
        string nonExistentDir = Path.Combine(_testDir, "no_config");
        var config = GlobalConfigService.Load(nonExistentDir);

        Assert.NotNull(config);
        Assert.Equal("gemini-3.5-flash-lite", config.DefaultModel);
        Assert.Equal("gemini", config.DefaultProvider);
        Assert.Equal(3, config.MaxRetries);
        Assert.False(config.AutoApprove);
    }

    [Fact]
    public void GlobalConfigService_SavesAndLoadsConfig_Correctly()
    {
        string customDir = Path.Combine(_testDir, "custom_config");
        var configToSave = new GlobalConfig
        {
            DefaultModel = "gpt-4o",
            DefaultProvider = "openai",
            MaxRetries = 5,
            AutoApprove = true,
            Language = "en"
        };

        GlobalConfigService.Save(configToSave, customDir);

        var loaded = GlobalConfigService.Load(customDir);
        Assert.NotNull(loaded);
        Assert.Equal("gpt-4o", loaded.DefaultModel);
        Assert.Equal("openai", loaded.DefaultProvider);
        Assert.Equal(5, loaded.MaxRetries);
        Assert.True(loaded.AutoApprove);
        Assert.Equal("en", loaded.Language);
    }

    [Fact]
    public void CliArgumentsParser_UsesGlobalConfigDefaults_WhenNoCliFlagsProvided()
    {
        var globalConfig = new GlobalConfig
        {
            DefaultModel = "gpt-4o-custom",
            DefaultProvider = "openai",
            MaxRetries = 4,
            AutoApprove = true
        };

        var options = CliArgumentsParser.Parse(Array.Empty<string>(), globalConfig);

        Assert.Equal("gpt-4o-custom", options.ModelId);
        Assert.Equal("openai", options.Provider);
        Assert.Equal(4, options.MaxRetries);
        Assert.True(options.AutoApprove);
    }

    [Fact]
    public void CliArgumentsParser_CliFlagsOverrideGlobalConfig()
    {
        var globalConfig = new GlobalConfig
        {
            DefaultModel = "gpt-4o",
            DefaultProvider = "openai",
            MaxRetries = 5,
            AutoApprove = false
        };

        var options = CliArgumentsParser.Parse(
            new[] { "--model", "gemini-pro", "--provider", "gemini", "--retries", "2", "--yes" },
            globalConfig
        );

        Assert.Equal("gemini-pro", options.ModelId);
        Assert.Equal("gemini", options.Provider);
        Assert.Equal(2, options.MaxRetries);
        Assert.True(options.AutoApprove);
    }

    #endregion

    #region Tarea 4.5: Modo Interactivo (--interactive)

    [Fact]
    public void CliArgumentsParser_ParsesInteractiveFlag_Correctly()
    {
        var options1 = CliArgumentsParser.Parse(new[] { "--interactive" });
        Assert.True(options1.Interactive);

        var options2 = CliArgumentsParser.Parse(new[] { "-i" });
        Assert.True(options2.Interactive);

        var options3 = CliArgumentsParser.Parse(Array.Empty<string>());
        Assert.False(options3.Interactive);
    }

    [Fact]
    public void AgentConfiguration_HoldsInteractiveAndProviderFlags()
    {
        var config = new AgentConfiguration(
            "dummy-key",
            "gemini-3.5-flash-lite",
            maxRetries: 3,
            autoApprove: false,
            provider: "openai",
            interactivePerMilestone: true
        );

        Assert.Equal("openai", config.Provider);
        Assert.True(config.InteractivePerMilestone);

        var context = new AgentContext("c:\\test", "dummy-key", provider: "anthropic", interactivePerMilestone: true);
        Assert.Equal("anthropic", context.Provider);
        Assert.True(context.InteractivePerMilestone);
    }

    [Fact]
    public async Task ConsoleFeedbackHandler_RequestMilestoneApprovalAsync_DefaultIsApproved()
    {
        IHumanFeedbackHandler handler = new ConsoleFeedbackHandler();
        var milestone = new PlanMilestone
        {
            Order = 1,
            Title = "Test Milestone",
            Description = "Description of test milestone"
        };

        var defaultDecision = await handler.RequestMilestoneApprovalAsync(milestone);
        Assert.Equal(HumanDecisionType.Approved, defaultDecision.Type);
    }

    #endregion
}
