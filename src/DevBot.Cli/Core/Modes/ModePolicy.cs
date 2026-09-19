namespace DevBot.Cli.Core.Modes;

public record ModePolicy(
    AgentMode Mode,
    string DisplayName,
    int DefaultMaxRetries,
    bool EnforceContractImmutability,
    bool RequireFailingTestFirst,
    string ScoutInstructions,
    string CoderInstructions,
    string ReviewerInstructions
);

public static class ModePolicyRegistry
{
    public static AgentMode ParseMode(string? modeStr)
    {
        if (string.IsNullOrWhiteSpace(modeStr)) return AgentMode.Feature;

        return modeStr.Trim().ToLowerInvariant() switch
        {
            "bug" or "bugfix" or "fix" => AgentMode.Bug,
            "refactor" or "clean" or "cleanup" => AgentMode.Refactor,
            "test" or "tests" or "coverage" => AgentMode.Test,
            "feature" or "feat" or _ => AgentMode.Feature
        };
    }

    /// <summary>
    /// Infiere automáticamente el modo de operación más adecuado analizando la descripción de la tarea.
    /// Si no se detectan patrones específicos, recurre a Feature por defecto.
    /// </summary>
    public static (AgentMode Mode, string Reason) DetectMode(string taskDescription)
    {
        if (string.IsNullOrWhiteSpace(taskDescription))
        {
            return (AgentMode.Feature, "Tarea no especificada, se asume Feature por defecto.");
        }

        string text = taskDescription.ToLowerInvariant();

        // 1. Patrones de Bug / Fix
        string[] bugKeywords = ["bug", "bugs", "error", "errores", "exception", "excepcion", "excepción", "fix", "corregir", "correccion", "corrección", "arreglar", "reparar", "falla", "fallas", "fallo", "fallos", "crash", "cae", "caida", "caída", "rompe", "500", "nullreference", "stacktrace", "issue", "defect"];
        foreach (var kw in bugKeywords)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(kw)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return (AgentMode.Bug, $"Detectado término de corrección: '{kw}'");
            }
        }

        // 2. Patrones de Test / Pruebas
        string[] testKeywords = ["test", "tests", "testing", "prueba", "pruebas", "cobertura", "coverage", "assert", "asserts", "assertion", "assertions", "mock", "mocks", "unit test", "test unitario"];
        foreach (var kw in testKeywords)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(kw)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return (AgentMode.Test, $"Detectado término de pruebas/QA: '{kw}'");
            }
        }

        // 3. Patrones de Refactor / Limpieza
        string[] refactorKeywords = ["refactor", "refactorizar", "limpiar", "cleanup", "optimizar", "rendimiento", "extraer", "deuda técnica", "simplificar", "desacoplar"];
        foreach (var kw in refactorKeywords)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(kw)}\b"))
            {
                return (AgentMode.Refactor, $"Detectado término de refactorización: '{kw}'");
            }
        }

        return (AgentMode.Feature, "No se identificaron patrones de bug, test o refactor; se asume Feature.");
    }

    public static ModePolicy GetPolicy(AgentMode mode)
    {
        return mode switch
        {
            AgentMode.Bug => new ModePolicy(
                Mode: AgentMode.Bug,
                DisplayName: "Bugfix / Diagnóstico Quirúrgico (bug)",
                DefaultMaxRetries: 3,
                EnforceContractImmutability: false,
                RequireFailingTestFirst: true,
                ScoutInstructions: """
                    [MODE: BUGFIX CONSTRAINTS]
                    - Focus strictly on locating the root cause of the bug or unexpected behavior.
                    - Identify the minimal number of lines/methods that must be altered to fix the issue.
                    - Specify a reproduction plan: how to reproduce the bug via a targeted unit test before fixing it.
                    """,
                CoderInstructions: """
                    [MODE: BUGFIX CONSTRAINTS]
                    - Apply MINIMAL, SURGICAL changes. Do NOT rewrite surrounding unrelated logic or perform opportunistic refactorings.
                    - If possible, write or update a reproduction test first that exposes the bug, then fix the defect so the test turns green.
                    - Avoid touching unrelated files or dependencies.
                    """,
                ReviewerInstructions: """
                    [MODE: BUGFIX VERIFICATION]
                    - Verify that the specific bug reported is completely resolved and that regression tests pass.
                    - Confirm that no unrelated test suites were broken by the fix.
                    """
            ),

            AgentMode.Refactor => new ModePolicy(
                Mode: AgentMode.Refactor,
                DisplayName: "Refactorización y Deuda Técnica (refactor)",
                DefaultMaxRetries: 3,
                EnforceContractImmutability: true,
                RequireFailingTestFirst: false,
                ScoutInstructions: """
                    [MODE: REFACTOR CONSTRAINTS]
                    - Identify code smells, technical debt, or architectural bottlenecks (e.g. duplication, high cyclomatic complexity, tight coupling).
                    - CONTRACT IMMUTABILITY: Document public signatures, endpoints, and DTOs that MUST NOT BE CHANGED.
                    - Ensure existing test suites adequately cover the refactoring target before modifying code.
                    """,
                CoderInstructions: """
                    [MODE: REFACTOR CONSTRAINTS - STRICT CONTRACT IMMUTABILITY]
                    - DO NOT alter any public contracts, API endpoint routes, JSON serialization models, or public method signatures.
                    - Focus exclusively on internal clean code, SOLID patterns, readability, extraction of methods/classes, and performance.
                    - All existing tests MUST remain green without changing existing test assertions to make them pass.
                    """,
                ReviewerInstructions: """
                    [MODE: REFACTOR VERIFICATION]
                    - Verify that all existing unit and integration tests pass 100% without modification to test assertions.
                    - Confirm that public contracts, interfaces, and signatures remained strictly immutable.
                    """
            ),

            AgentMode.Test => new ModePolicy(
                Mode: AgentMode.Test,
                DisplayName: "Generación de Pruebas y Cobertura (test)",
                DefaultMaxRetries: 2,
                EnforceContractImmutability: true,
                RequireFailingTestFirst: false,
                ScoutInstructions: """
                    [MODE: TEST GENERATION CONSTRAINTS]
                    - Scan for modules, services, or edge cases that lack test coverage.
                    - Map out target test files to create or extend in the project's test directory.
                    - Propose test cases covering: happy path, boundary conditions, null inputs, and error/exception scenarios.
                    """,
                CoderInstructions: """
                    [MODE: TEST GENERATION CONSTRAINTS]
                    - You MUST NOT modify business logic files or production code.
                    - Create or edit ONLY test files in the test suite directory (e.g., `tests/`, `test/`).
                    - Use the project's established test framework and mocking conventions (Arrange-Act-Assert).
                    """,
                ReviewerInstructions: """
                    [MODE: TEST VERIFICATION]
                    - Verify that all newly created and pre-existing tests execute and pass cleanly.
                    - Ensure the new tests test meaningful behavior and are not flaky or tautological.
                    """
            ),

            _ => new ModePolicy(
                Mode: AgentMode.Feature,
                DisplayName: "Desarrollo de Funcionalidad (feature)",
                DefaultMaxRetries: 3,
                EnforceContractImmutability: false,
                RequireFailingTestFirst: false,
                ScoutInstructions: """
                    [MODE: FEATURE IMPLEMENTATION]
                    - Analyze the feature requirements thoroughly against existing patterns.
                    - Plan target files, architectural extensions, and unit test strategy to ensure full functionality.
                    """,
                CoderInstructions: """
                    [MODE: FEATURE IMPLEMENTATION]
                    - Implement the complete feature following the Scout architectural plan.
                    - Include accompanying unit or integration tests that validate the new behavior.
                    """,
                ReviewerInstructions: """
                    [MODE: FEATURE VERIFICATION]
                    - Verify that the new feature passes all tests and meets the requirement specification.
                    """
            )
        };
    }
}
