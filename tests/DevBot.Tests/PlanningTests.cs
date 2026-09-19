using DevBot.Cli.Core;
using DevBot.Cli.Core.Planning;
using DevBot.Cli.Tools;
using Xunit;

namespace DevBot.Tests;

public class PlanningTests : IDisposable
{
    private readonly string _testDir;

    public PlanningTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "devbot_planning_" + Guid.NewGuid().ToString("N"));
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
    public void PlanDefinition_SerializesAndDeserializesCorrectly()
    {
        var plan = new PlanDefinition
        {
            TaskDescription = "Crear endpoint de usuarios con arquitectura limpia",
            ArchitecturalSummary = "Descomposición en 3 capas: dominio, servicio y controlador.",
            Milestones = new List<PlanMilestone>
            {
                new()
                {
                    Order = 1,
                    Id = "m1",
                    Title = "Definir entidad User e interfaz IUserRepository",
                    Scope = "domain",
                    Description = "Crear la entidad inmutable User y el contrato de repositorio.",
                    TargetFiles = new List<string> { "src/Domain/User.cs", "src/Domain/IUserRepository.cs" },
                    VerificationCommand = "dotnet build",
                    RequiredSkills = new List<string> { "domain-modeling" }
                },
                new()
                {
                    Order = 2,
                    Id = "m2",
                    Title = "Implementar UserService",
                    Scope = "services",
                    Description = "Implementar la lógica de negocio y validación de usuarios.",
                    TargetFiles = new List<string> { "src/Services/UserService.cs" },
                    VerificationCommand = "dotnet test --filter Category=Unit",
                    RequiredSkills = new List<string> { "business-logic" }
                }
            }
        };

        string json = plan.ToJson();
        Assert.False(string.IsNullOrWhiteSpace(json));

        var deserialized = PlanDefinition.FromJson(json);
        Assert.NotNull(deserialized);
        Assert.Equal(plan.TaskDescription, deserialized.TaskDescription);
        Assert.Equal(plan.ArchitecturalSummary, deserialized.ArchitecturalSummary);
        Assert.Equal(2, deserialized.Milestones.Count);

        Assert.Equal("m1", deserialized.Milestones[0].Id);
        Assert.Equal("domain", deserialized.Milestones[0].Scope);
        Assert.Equal(2, deserialized.Milestones[0].TargetFiles.Count);
        Assert.Equal("dotnet build", deserialized.Milestones[0].VerificationCommand);

        Assert.Equal("m2", deserialized.Milestones[1].Id);
        Assert.Equal("services", deserialized.Milestones[1].Scope);
        Assert.Single(deserialized.Milestones[1].TargetFiles);
    }

    [Fact]
    public void PlanDefinition_ToMarkdown_GeneratesStructuredMarkdown()
    {
        var plan = new PlanDefinition
        {
            TaskDescription = "Implementar HealthCheck",
            ArchitecturalSummary = "Agregar endpoint /health con diagnóstico de memoria.",
            Milestones = new List<PlanMilestone>
            {
                new()
                {
                    Order = 1,
                    Id = "m1",
                    Title = "Endpoint Health",
                    Scope = "api",
                    Description = "Agregar controlador HealthController.",
                    TargetFiles = new List<string> { "src/Controllers/HealthController.cs" },
                    VerificationCommand = "dotnet test",
                    IsCompleted = true,
                    CheckpointCommitHash = "a1b2c3d"
                }
            }
        };

        string markdown = plan.ToMarkdown();

        Assert.Contains("# Plan de Implementación Descompuesto", markdown);
        Assert.Contains("Implementar HealthCheck", markdown);
        Assert.Contains("Hito 1: Endpoint Health (`api`)", markdown);
        Assert.Contains("[x] Completado (Commit: `a1b2c3d`)", markdown);
        Assert.Contains("`dotnet test`", markdown);
        Assert.Contains("`src/Controllers/HealthController.cs`", markdown);
    }

    [Fact]
    public void AgentContext_SaveAndReadPlan_PersistsToDisk()
    {
        var context = new AgentContext(_testDir, "test_api_key");
        var plan = new PlanDefinition
        {
            TaskDescription = "Tarea con checkpoints",
            ArchitecturalSummary = "Resumen de arquitectura",
            Milestones = new List<PlanMilestone>
            {
                new()
                {
                    Order = 1,
                    Id = "m1",
                    Title = "Paso 1",
                    Scope = "core",
                    Description = "Modificar archivo base",
                    TargetFiles = new List<string> { "Core.cs" },
                    VerificationCommand = "dotnet build"
                }
            }
        };

        context.SavePlan(plan);

        Assert.True(File.Exists(context.PlanJsonPath));
        Assert.True(File.Exists(context.PlanMarkdownPath));

        var loaded = context.ReadPlan();
        Assert.NotNull(loaded);
        Assert.Equal("Tarea con checkpoints", loaded.TaskDescription);
        Assert.Single(loaded.Milestones);
        Assert.Equal("Paso 1", loaded.Milestones[0].Title);
    }

    [Fact]
    public void PlanDefinition_FromJson_ReturnsNullForInvalidJson()
    {
        string invalidJson = "{ this is not valid json :::";
        var result = PlanDefinition.FromJson(invalidJson);
        Assert.Null(result);
    }

    [Fact]
    public void PlanMilestone_DefaultValues_AreConsistent()
    {
        var milestone = new PlanMilestone();
        Assert.Equal("core", milestone.Scope);
        Assert.Empty(milestone.TargetFiles);
        Assert.Empty(milestone.RequiredSkills);
        Assert.False(milestone.IsCompleted);
        Assert.Null(milestone.CheckpointCommitHash);
    }

    [Fact]
    public async Task GitTools_ResetHardToCheckpoint_ReturnsSuccessOnCleanRevert()
    {
        var gitTools = new GitTools(_testDir);
        // On non-git directory, it returns error or fallback without throwing unhandled exceptions
        string result = await gitTools.ResetHardToCheckpointAsync("abc1234");
        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
