using DevBot.Cli.Core;
using DevBot.Cli.Core.Configuration;
using DevBot.Cli.Core.DependencyInjection;
using DevBot.Cli.Prompts;
using DevBot.Cli.Subagents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevBot.Tests;

public class Phase6Tests
{
    #region Tarea 6.1: Subcomandos de CLI y Ergonomía

    [Fact]
    public void CliArgumentsParser_ParsesRunSubcommand_WithPositionalTask()
    {
        var options = CliArgumentsParser.Parse(new[] { "run", "Implementar endpoint de métricas", "-d", "./MiRepo" });

        Assert.Equal("run", options.Subcommand);
        Assert.Equal("Implementar endpoint de métricas", options.TaskDescription);
        Assert.Equal("./MiRepo", options.RepoDir);
    }

    [Fact]
    public void CliArgumentsParser_ParsesRunSubcommand_WithExplicitTaskFlag()
    {
        var options = CliArgumentsParser.Parse(new[] { "run", "-t", "Refactorizar modulo de pagos" });

        Assert.Equal("run", options.Subcommand);
        Assert.Equal("Refactorizar modulo de pagos", options.TaskDescription);
    }

    [Fact]
    public void CliArgumentsParser_ParsesReportSubcommand_Correctly()
    {
        var options = CliArgumentsParser.Parse(new[] { "report" });

        Assert.Equal("report", options.Subcommand);
        Assert.True(options.ShowReport);
    }

    [Fact]
    public void CliArgumentsParser_ParsesLogsSubcommand_Correctly()
    {
        var options1 = CliArgumentsParser.Parse(new[] { "logs" });
        Assert.Equal("logs", options1.Subcommand);
        Assert.True(options1.ShowLogs);

        var options2 = CliArgumentsParser.Parse(new[] { "log" });
        Assert.Equal("logs", options2.Subcommand);
        Assert.True(options2.ShowLogs);
    }

    [Fact]
    public void CliArgumentsParser_ParsesStatsSubcommand_Correctly()
    {
        var options = CliArgumentsParser.Parse(new[] { "stats" });

        Assert.Equal("stats", options.Subcommand);
        Assert.True(options.ShowStats);
    }

    [Fact]
    public void CliArgumentsParser_ParsesTestSubcommand_Correctly()
    {
        var options = CliArgumentsParser.Parse(new[] { "test" });

        Assert.Equal("test", options.Subcommand);
        Assert.True(options.TestOnly);
    }

    [Fact]
    public void CliArgumentsParser_ParsesHelpSubcommand_Correctly()
    {
        var options = CliArgumentsParser.Parse(new[] { "help" });

        Assert.Equal("help", options.Subcommand);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void CliArgumentsParser_PrintHelp_ExecutesWithoutException()
    {
        // Verifica que la renderización de ayuda no lance excepciones
        var exception = Record.Exception(() => CliArgumentsParser.PrintHelp());
        Assert.Null(exception);
    }

    #endregion

    #region Tarea 6.2: Argumentos que inician con guión o números negativos

    [Fact]
    public void CliArgumentsParser_AcceptsTaskStartingWithHyphen()
    {
        var options = CliArgumentsParser.Parse(new[] { "-t", "- Corregir validación de email en SignUp" });

        Assert.Equal("- Corregir validación de email en SignUp", options.TaskDescription);
    }

    [Fact]
    public void CliArgumentsParser_AcceptsTaskStartingWithNegativeNumber()
    {
        var options = CliArgumentsParser.Parse(new[] { "--task", "-50 offset de paginación bug" });

        Assert.Equal("-50 offset de paginación bug", options.TaskDescription);
    }

    [Fact]
    public void CliArgumentsParser_KnownFlagFollowingValueFlag_IsNotCapturedAsValue()
    {
        // Si el usuario pasa -t seguido inmediatamente de -y (olvidó la tarea), no debe capturar "-y" como la tarea
        var options = CliArgumentsParser.Parse(new[] { "-t", "-y" });

        Assert.Null(options.TaskDescription);
        Assert.True(options.AutoApprove);
    }

    #endregion

    #region Tarea 6.3: Consistencia de Idioma Prompts ↔ Consola

    [Fact]
    public void PromptLoader_LoadsFallback_WhenFileDoesNotExist()
    {
        string fallback = "You are an autonomous AI coding assistant. Follow instructions carefully.";
        string loaded = PromptLoader.LoadPrompt("non_existent_prompt_xyz.txt", fallback);

        Assert.Equal(fallback, loaded);
    }

    #endregion

    #region Tarea 6.4: Inversión de Dependencias en Orchestrator

    [Fact]
    public void Orchestrator_CannotBeInstantiatedWithoutContainerDependencies()
    {
        // Verifica que Orchestrator solo expone constructores con dependencias inyectadas
        var constructors = typeof(Orchestrator).GetConstructors();
        Assert.Single(constructors);

        var parameters = constructors[0].GetParameters();
        // Debe requerir AgentContext, IScoutAgent, ICoderAgent, etc. (no un constructor que tome solo AgentContext)
        Assert.True(parameters.Length > 1);
        Assert.Contains(parameters, p => p.ParameterType == typeof(AgentContext));
        Assert.Contains(parameters, p => p.ParameterType == typeof(IScoutAgent));
    }

    [Fact]
    public void OrchestratorFactory_SuccessfullyBuildsOrchestratorThroughDI()
    {
        var services = new ServiceCollection();
        services.AddDevBotCore();
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IOrchestratorFactory>();
        var context = new AgentContext("C:\\repo", "test_key", "test_model");

        var orchestrator = factory.Create(context);
        Assert.NotNull(orchestrator);
    }

    #endregion
}
