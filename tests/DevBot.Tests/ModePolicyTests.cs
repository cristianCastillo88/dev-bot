using DevBot.Cli.Core;
using DevBot.Cli.Core.Modes;
using Xunit;

namespace DevBot.Tests;

public class ModePolicyTests
{
    [Theory]
    [InlineData("feature", AgentMode.Feature)]
    [InlineData("feat", AgentMode.Feature)]
    [InlineData("", AgentMode.Feature)]
    [InlineData(null, AgentMode.Feature)]
    [InlineData("unknown_value", AgentMode.Feature)]
    [InlineData("bug", AgentMode.Bug)]
    [InlineData("bugfix", AgentMode.Bug)]
    [InlineData("fix", AgentMode.Bug)]
    [InlineData("BUG", AgentMode.Bug)]
    [InlineData("refactor", AgentMode.Refactor)]
    [InlineData("clean", AgentMode.Refactor)]
    [InlineData("cleanup", AgentMode.Refactor)]
    [InlineData("test", AgentMode.Test)]
    [InlineData("tests", AgentMode.Test)]
    [InlineData("coverage", AgentMode.Test)]
    public void ParseMode_MapsKeywordsProperly(string? input, AgentMode expectedMode)
    {
        var result = ModePolicyRegistry.ParseMode(input);
        Assert.Equal(expectedMode, result);
    }

    [Fact]
    public void BugMode_RequiresReproductionTestAndMinimalDiff()
    {
        var policy = ModePolicyRegistry.GetPolicy(AgentMode.Bug);

        Assert.Equal(AgentMode.Bug, policy.Mode);
        Assert.True(policy.RequireFailingTestFirst);
        Assert.Contains("reproduction", policy.ScoutInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MINIMAL", policy.CoderInstructions);
        Assert.Contains("regression", policy.ReviewerInstructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefactorMode_EnforcesStrictContractImmutability()
    {
        var policy = ModePolicyRegistry.GetPolicy(AgentMode.Refactor);

        Assert.Equal(AgentMode.Refactor, policy.Mode);
        Assert.True(policy.EnforceContractImmutability);
        Assert.Contains("CONTRACT IMMUTABILITY", policy.ScoutInstructions);
        Assert.Contains("DO NOT alter any public contracts", policy.CoderInstructions);
        Assert.Contains("immutable", policy.ReviewerInstructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestMode_ForbidsProductionCodeEdits()
    {
        var policy = ModePolicyRegistry.GetPolicy(AgentMode.Test);

        Assert.Equal(AgentMode.Test, policy.Mode);
        Assert.True(policy.EnforceContractImmutability);
        Assert.Contains("lack test coverage", policy.ScoutInstructions);
        Assert.Contains("MUST NOT modify business logic", policy.CoderInstructions);
    }

    [Fact]
    public void AgentContext_SetMode_UpdatesModeAndPolicy()
    {
        var context = new AgentContext(Path.GetTempPath(), "dummy-key");
        Assert.Equal(AgentMode.Feature, context.Mode);

        context.SetMode(AgentMode.Refactor);
        Assert.Equal(AgentMode.Refactor, context.Mode);
        Assert.True(context.ModePolicy.EnforceContractImmutability);
        Assert.Contains("Refactorización", context.ModePolicy.DisplayName);

        context.SetMode(AgentMode.Bug);
        Assert.Equal(AgentMode.Bug, context.Mode);
        Assert.True(context.ModePolicy.RequireFailingTestFirst);
    }

    [Theory]
    [InlineData("Arreglar bug en el login cuando el token expira", AgentMode.Bug)]
    [InlineData("Corregir NullReferenceException al procesar el pago", AgentMode.Bug)]
    [InlineData("Fix 500 error en el controlador de usuarios", AgentMode.Bug)]
    [InlineData("Se cae el microservicio cuando la cola está vacía", AgentMode.Bug)]
    [InlineData("Agregar tests unitarios para FacturacionService", AgentMode.Test)]
    [InlineData("Aumentar cobertura de pruebas en el módulo de autenticación", AgentMode.Test)]
    [InlineData("Escribir asserts para validar el cálculo de impuestos", AgentMode.Test)]
    [InlineData("Refactorizar la clase OrderManager para reducir complejidad ciclomática", AgentMode.Refactor)]
    [InlineData("Limpiar código duplicado y extraer clase Calculator", AgentMode.Refactor)]
    [InlineData("Optimizar rendimiento del query de clientes", AgentMode.Refactor)]
    [InlineData("Crear endpoint POST /api/v1/orders para recibir pedidos", AgentMode.Feature)]
    [InlineData("Implementar integración con pasarela de MercadoPago", AgentMode.Feature)]
    [InlineData("", AgentMode.Feature)]
    [InlineData(null, AgentMode.Feature)]
    public void DetectMode_InfersAppropriateModeFromTaskDescription(string? taskDescription, AgentMode expectedMode)
    {
        var (detectedMode, reason) = ModePolicyRegistry.DetectMode(taskDescription!);

        Assert.Equal(expectedMode, detectedMode);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}
