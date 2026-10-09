using DevBot.Cli.Core;
using DevBot.Cli.Core.Configuration;
using DevBot.Cli.Core.Diagnostics;
using DevBot.Cli.Core.Memory;
using DevBot.Cli.Core.Planning;
using Xunit;

namespace DevBot.Tests;

public class Phase5FeaturesTests : IDisposable
{
    private readonly string _testDir;

    public Phase5FeaturesTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "devbot_p5_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    #region Tarea 5.1: Modo --test-only

    [Fact]
    public void CliArgumentsParser_ParsesTestOnlyFlag_Correctly()
    {
        var options1 = CliArgumentsParser.Parse(new[] { "--test-only" });
        Assert.True(options1.TestOnly);

        var options2 = CliArgumentsParser.Parse(Array.Empty<string>());
        Assert.False(options2.TestOnly);
    }

    [Fact]
    public async Task TestRunnerService_DetectsStackAndReturnsCode()
    {
        // En un directorio vacío sin proyecto, fallback strategy debe retornar código de error o no romperse
        int exitCode = await TestRunnerService.RunTestsAsync(_testDir);
        // Fallback strategy no encuentra build tool
        Assert.True(exitCode >= 0);
    }

    #endregion

    #region Tarea 5.2: Soporte .devbotrules.yaml

    [Fact]
    public async Task RepositoryMemoryService_LoadLocalRulesAsync_ReadsYamlListFormat()
    {
        string yamlContent = """
            - Usar siempre C# 12
            - Mantener métodos bajo 30 líneas
            - Agregar XML docs
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, ".devbotrules.yaml"), yamlContent);

        var service = new RepositoryMemoryService();
        string? rules = await service.LoadLocalRulesAsync(_testDir);

        Assert.NotNull(rules);
        Assert.Contains("- Usar siempre C# 12", rules);
        Assert.Contains("- Mantener métodos bajo 30 líneas", rules);
        Assert.Contains("- Agregar XML docs", rules);
    }

    [Fact]
    public async Task RepositoryMemoryService_LoadLocalRulesAsync_ReadsYamlDictionaryFormat()
    {
        string yamlContent = """
            rules:
              - No usar dynamic
              - Priorizar inmutabilidad
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, ".devbotrules.yml"), yamlContent);

        var service = new RepositoryMemoryService();
        string? rules = await service.LoadLocalRulesAsync(_testDir);

        Assert.NotNull(rules);
        Assert.Contains("- No usar dynamic", rules);
        Assert.Contains("- Priorizar inmutabilidad", rules);
    }

    #endregion

    #region Tarea 5.3: Dashboard devbot stats

    [Fact]
    public void CliArgumentsParser_ParsesStatsFlag_Correctly()
    {
        var options1 = CliArgumentsParser.Parse(new[] { "--stats" });
        Assert.True(options1.ShowStats);

        var options2 = CliArgumentsParser.Parse(new[] { "stats" });
        Assert.True(options2.ShowStats);

        var options3 = CliArgumentsParser.Parse(Array.Empty<string>());
        Assert.False(options3.ShowStats);
    }

    [Fact]
    public void HistoricalStatsService_LoadStats_ReturnsEmpty_WhenNoReports()
    {
        var stats = HistoricalStatsService.LoadStats(_testDir);
        Assert.Empty(stats);
    }

    [Fact]
    public void HistoricalStatsService_LoadStats_ParsesReports_Correctly()
    {
        string reportsDir = Path.Combine(_testDir, ".agent", "reports");
        Directory.CreateDirectory(reportsDir);

        string sampleReport = """
            # 🤖 DevBot - Informe Ejecutivo de Ejecución

            > **Fecha y Hora:** 2026-10-09 10:15:30  
            > **Modelo IA:** `gemini-3.5-flash-lite`  
            > **Estado:** **✔ COMPLETADO CON ÉXITO (ALL_PASS)**  

            ## 2. Requerimiento Asignado

            ```text
            Agregar endpoint GET /health y tests unitarios
            ```

            ## 4. Desglose de Métricas y Telemetría por Agente

            - **Tiempo Total de Ejecución:** 42.5 segundos
            - **Tokens Totales Consumidos:** 15,400
            - **Ciclos Coder/Reviewer Requeridos:** 1
            """;

        File.WriteAllText(Path.Combine(reportsDir, "report_20261009_101530.md"), sampleReport);

        var stats = HistoricalStatsService.LoadStats(_testDir);
        Assert.Single(stats);

        var run = stats[0];
        Assert.Equal("2026-10-09 10:15:30", run.Timestamp);
        Assert.Equal("Agregar endpoint GET /health y tests unitarios", run.TaskDescription);
        Assert.True(run.Success);
        Assert.Contains("42.5", run.Duration);
        Assert.Equal(1, run.Retries);

        int exitCode = HistoricalStatsService.ShowStats(_testDir);
        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Tarea 5.4: Audit log con firma de integridad (hash chain)

    [Fact]
    public void AgentLogger_GeneratesValidHashChain_ForMultipleEntries()
    {
        var logger = new AgentLogger(_testDir);
        logger.LogEvent("Scout", "PhaseStart", "Iniciando análisis");
        logger.LogEvent("Coder", "ToolCall", "ReadFile Program.cs");
        logger.LogEvent("Reviewer", "PhaseEnd", "All tests passed");

        var entries = logger.GetEntries();
        Assert.Equal(3, entries.Count);

        // PreviousHash de la primera entrada es el hash génesis
        Assert.Equal("0000000000000000000000000000000000000000000000000000000000000000", entries[0].PreviousHash);
        Assert.False(string.IsNullOrEmpty(entries[0].Hash));

        // Segunda entrada encadenada a la primera
        Assert.Equal(entries[0].Hash, entries[1].PreviousHash);

        // Tercera entrada encadenada a la segunda
        Assert.Equal(entries[1].Hash, entries[2].PreviousHash);

        // La verificación de integridad de la sesión debe ser válida
        var (isValid, errorMessage) = logger.VerifyCurrentSessionIntegrity();
        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void AgentLogger_VerifyIntegrity_DetectsTamperedMessage()
    {
        var logger = new AgentLogger(_testDir);
        logger.LogEvent("Scout", "PhaseStart", "Iniciando análisis");
        logger.LogEvent("Coder", "ToolCall", "ReadFile Program.cs");

        var entries = new List<LogEntry>(logger.GetEntries());

        // Alteramos maliciosamente el mensaje de la primera entrada
        entries[0].Message = "Mensaje alterado no autorizado";

        var (isValid, errorMessage) = AgentLogger.VerifyIntegrity(entries);
        Assert.False(isValid);
        Assert.NotNull(errorMessage);
        Assert.Contains("contenido alterado", errorMessage);
    }

    [Fact]
    public void AgentLogger_VerifyIntegrity_DetectsTamperedPreviousHash()
    {
        var logger = new AgentLogger(_testDir);
        logger.LogEvent("Scout", "PhaseStart", "Evento 1");
        logger.LogEvent("Coder", "ToolCall", "Evento 2");

        var entries = new List<LogEntry>(logger.GetEntries());

        // Alteramos el previous hash de la segunda entrada
        entries[1].PreviousHash = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

        var (isValid, errorMessage) = AgentLogger.VerifyIntegrity(entries);
        Assert.False(isValid);
        Assert.NotNull(errorMessage);
        Assert.Contains("Discrepancia en PreviousHash", errorMessage);
    }

    #endregion

    #region Tarea 5.5: Ejecución y dependencias (GetParallelGroups)

    [Fact]
    public void PlanDefinition_GetParallelGroups_ReturnsSequentialBatches_WhenNoDependencies()
    {
        var plan = new PlanDefinition
        {
            Milestones = new List<PlanMilestone>
            {
                new() { Order = 1, Title = "Hito A" },
                new() { Order = 2, Title = "Hito B" },
                new() { Order = 3, Title = "Hito C" }
            }
        };

        var batches = plan.GetParallelGroups();
        Assert.Equal(3, batches.Count);
        Assert.Single(batches[0]);
        Assert.Equal("Hito A", batches[0][0].Title);
        Assert.Single(batches[1]);
        Assert.Equal("Hito B", batches[1][0].Title);
        Assert.Single(batches[2]);
        Assert.Equal("Hito C", batches[2][0].Title);
    }

    [Fact]
    public void PlanDefinition_GetParallelGroups_ResolvesTopologicalLayers_WithDependencies()
    {
        var plan = new PlanDefinition
        {
            Milestones = new List<PlanMilestone>
            {
                new() { Order = 1, Id = "M1", Title = "Setup Database" },
                new() { Order = 2, Id = "M2", Title = "Create User Repository", DependsOn = new() { "M1" } },
                new() { Order = 3, Id = "M3", Title = "Create Product Repository", DependsOn = new() { "M1" } },
                new() { Order = 4, Id = "M4", Title = "Integrate API Endpoints", DependsOn = new() { "M2", "M3" } }
            }
        };

        var batches = plan.GetParallelGroups();

        // Esperamos 3 oleadas:
        // Oleada 1: [M1]
        // Oleada 2: [M2, M3] (independientes entre sí, dependen de M1)
        // Oleada 3: [M4] (depende de M2 y M3)
        Assert.Equal(3, batches.Count);

        Assert.Single(batches[0]);
        Assert.Equal("M1", batches[0][0].Id);

        Assert.Equal(2, batches[1].Count);
        var batch2Ids = batches[1].Select(m => m.Id).ToList();
        Assert.Contains("M2", batch2Ids);
        Assert.Contains("M3", batch2Ids);

        Assert.Single(batches[2]);
        Assert.Equal("M4", batches[2][0].Id);
    }

    [Fact]
    public void PlanDefinition_ToMarkdown_IncludesDependencies()
    {
        var plan = new PlanDefinition
        {
            TaskDescription = "Demo task",
            ArchitecturalSummary = "Demo summary",
            Milestones = new List<PlanMilestone>
            {
                new() { Order = 1, Id = "M1", Title = "Base" },
                new() { Order = 2, Id = "M2", Title = "Derived", DependsOn = new() { "M1" } }
            }
        };

        string markdown = plan.ToMarkdown();
        Assert.Contains("- **Dependencias:** M1", markdown);
    }

    #endregion
}
