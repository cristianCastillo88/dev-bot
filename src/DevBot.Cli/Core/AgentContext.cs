using DevBot.Cli.Core.Auditing;
using DevBot.Cli.Core.Interactive;
using DevBot.Cli.Core.Memory;
using DevBot.Cli.Core.Modes;
using DevBot.Cli.Core.Planning;
using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core;

/// <summary>
/// Bus de estado centralizado, mutable y compartido para el ciclo de vida de una ejecución en DevBot.
/// Agrupa rutas de trabajo, parámetros de configuración, políticas operacionales, 
/// telemetría, dependencias de compilación y persistencia de artefactos en disco.
/// </summary>
public class AgentContext
{
    // Rutas absolutas del repositorio y espacio de trabajo de la ejecución
    public string RepoRoot { get; }
    public string AgentDir { get; }
    public string TaskFilePath { get; }
    public string ScoutReportPath { get; }
    public string ReviewResultsPath { get; }
    public string LatestReportPath => Path.Combine(AgentDir, "latest_run_report.md");
    public string ReportsDir => Path.Combine(AgentDir, "reports");
    public string PlanJsonPath => Path.Combine(AgentDir, "plan.json");
    public string PlanMarkdownPath => Path.Combine(AgentDir, "plan.md");

    // Parámetros y credenciales del LLM
    public string ApiKey { get; }
    public string ModelId { get; set; } = "gemini-3.5-flash-lite";
    public int MaxRetries { get; set; } = 3;

    // Estado de la misión y ramas Git
    public string TaskDescription { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public string OriginalBranch { get; set; } = string.Empty;
    public string? PullRequestUrl { get; set; }

    // Política y Modo de Operación
    public AgentMode Mode { get; private set; } = AgentMode.Feature;
    public ModePolicy ModePolicy { get; private set; } = ModePolicyRegistry.GetPolicy(AgentMode.Feature);

    // Human-in-the-Loop y Retroalimentación
    public bool AutoApprove { get; set; }
    public string? AdditionalUserGuidance { get; set; }
    public IHumanFeedbackHandler FeedbackHandler { get; set; } = new ConsoleFeedbackHandler();

    // Rastreabilidad de Modificaciones y Comandos
    public HashSet<string> ModifiedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int? LastCommandExitCode { get; set; }
    public List<(string Command, int ExitCode, bool Success)> CommandExecutionHistory { get; } = [];

    // Estrategia Políglota y Stack Detectado
    public ProjectStackInfo StackInfo { get; set; } = new(
        ProjectStackType.DotNet, "C#", "dotnet", null, [], []);
    public IBuildAndTestStrategy Strategy { get; set; } = new DotNetStrategy();

    // Auditoría de Seguridad y Calidad
    public IPreCommitAuditor Auditor { get; set; } = new PreCommitAuditor();

    // Memoria y Reglas de Repositorio (.devbotrules)
    public string? LocalRules { get; set; }
    public RepoMap? RepoMap { get; set; }
    public IRepositoryMemoryService MemoryService { get; set; } = new RepositoryMemoryService();

    // Descomposición en Hitos
    public PlanDefinition? Plan { get; set; }
    public PlanMilestone? CurrentMilestone { get; set; }
    public HashSet<string> MilestoneModifiedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Métricas y Registro Estructurado
    public ExecutionMetrics Metrics { get; } = new();
    public AgentLogger Logger { get; }

    /// <summary>
    /// Constructor principal que inicializa el contexto de ejecución y crea el directorio de artefactos si es necesario.
    /// </summary>
    /// <param name="repoRoot">Ruta raíz del repositorio de destino.</param>
    /// <param name="apiKey">Clave API para la conexión con el modelo de lenguaje.</param>
    /// <param name="modelId">Identificador del modelo de lenguaje (por defecto gemini-3.5-flash-lite).</param>
    public AgentContext(string repoRoot, string apiKey, string? modelId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        RepoRoot = Path.GetFullPath(repoRoot);
        AgentDir = Path.Combine(RepoRoot, ".agent");
        TaskFilePath = Path.Combine(AgentDir, "task.md");
        ScoutReportPath = Path.Combine(AgentDir, "scout_report.md");
        ReviewResultsPath = Path.Combine(AgentDir, "review_results.md");

        EnsureAgentDirectory();
        Logger = new AgentLogger(AgentDir);

        ApiKey = apiKey;
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            ModelId = modelId;
        }
    }

    /// <summary>
    /// Actualiza el modo operacional activo y su política de restricciones asociada de forma atómica.
    /// </summary>
    /// <param name="mode">Modo a establecer (Feature, Bug, Refactor, Test).</param>
    public void SetMode(AgentMode mode)
    {
        Mode = mode;
        ModePolicy = ModePolicyRegistry.GetPolicy(mode);
    }

    /// <summary>
    /// Registra en el historial el resultado de la ejecución de un comando de terminal.
    /// </summary>
    public void RecordCommandExecution(string command, int exitCode)
    {
        LastCommandExitCode = exitCode;
        CommandExecutionHistory.Add((command, exitCode, exitCode == 0));
    }

    /// <summary>
    /// Garantiza la existencia del directorio transitorio .agent en el repositorio.
    /// </summary>
    public void EnsureAgentDirectory()
    {
        if (!Directory.Exists(AgentDir))
        {
            Directory.CreateDirectory(AgentDir);
        }
    }

    // =========================================================================
    // Métodos Asíncronos de Persistencia (Modern .NET 8 con CancellationToken)
    // =========================================================================

    public async Task SaveTaskAsync(string taskDescription, CancellationToken ct = default)
    {
        EnsureAgentDirectory();
        TaskDescription = taskDescription;
        await File.WriteAllTextAsync(TaskFilePath, taskDescription, ct);
    }

    public async Task<string> ReadTaskAsync(CancellationToken ct = default)
    {
        return File.Exists(TaskFilePath) ? await File.ReadAllTextAsync(TaskFilePath, ct) : string.Empty;
    }

    public async Task SaveScoutReportAsync(string report, CancellationToken ct = default)
    {
        EnsureAgentDirectory();
        await File.WriteAllTextAsync(ScoutReportPath, report, ct);
    }

    public async Task<string> ReadScoutReportAsync(CancellationToken ct = default)
    {
        return File.Exists(ScoutReportPath) ? await File.ReadAllTextAsync(ScoutReportPath, ct) : string.Empty;
    }

    public async Task SaveReviewResultsAsync(string results, CancellationToken ct = default)
    {
        EnsureAgentDirectory();
        await File.WriteAllTextAsync(ReviewResultsPath, results, ct);
    }

    public async Task<string> ReadReviewResultsAsync(CancellationToken ct = default)
    {
        return File.Exists(ReviewResultsPath) ? await File.ReadAllTextAsync(ReviewResultsPath, ct) : string.Empty;
    }

    public async Task SavePlanAsync(PlanDefinition plan, CancellationToken ct = default)
    {
        EnsureAgentDirectory();
        Plan = plan;
        await File.WriteAllTextAsync(PlanJsonPath, plan.ToJson(), ct);
        await File.WriteAllTextAsync(PlanMarkdownPath, plan.ToMarkdown(), ct);
    }

    public async Task<PlanDefinition?> ReadPlanAsync(CancellationToken ct = default)
    {
        if (File.Exists(PlanJsonPath))
        {
            string json = await File.ReadAllTextAsync(PlanJsonPath, ct);
            return PlanDefinition.FromJson(json);
        }
        return null;
    }

    // =========================================================================
    // Wrappers Sincrónicos (Compatibilidad 100% con tests y código base actual)
    // =========================================================================

    public void SaveTask(string taskDescription)
    {
        EnsureAgentDirectory();
        TaskDescription = taskDescription;
        File.WriteAllText(TaskFilePath, taskDescription);
    }

    public string ReadTask() => File.Exists(TaskFilePath) ? File.ReadAllText(TaskFilePath) : string.Empty;

    public void SaveScoutReport(string report)
    {
        EnsureAgentDirectory();
        File.WriteAllText(ScoutReportPath, report);
    }

    public string ReadScoutReport() => File.Exists(ScoutReportPath) ? File.ReadAllText(ScoutReportPath) : string.Empty;

    public void SaveReviewResults(string results)
    {
        EnsureAgentDirectory();
        File.WriteAllText(ReviewResultsPath, results);
    }

    public string ReadReviewResults() => File.Exists(ReviewResultsPath) ? File.ReadAllText(ReviewResultsPath) : string.Empty;

    public void SavePlan(PlanDefinition plan)
    {
        EnsureAgentDirectory();
        Plan = plan;
        File.WriteAllText(PlanJsonPath, plan.ToJson());
        File.WriteAllText(PlanMarkdownPath, plan.ToMarkdown());
    }

    public PlanDefinition? ReadPlan()
    {
        if (File.Exists(PlanJsonPath))
        {
            return PlanDefinition.FromJson(File.ReadAllText(PlanJsonPath));
        }
        return null;
    }

    /// <summary>
    /// Limpia los artefactos temporales eliminando el directorio .agent si existe.
    /// </summary>
    public void CleanAgentArtifacts()
    {
        if (Directory.Exists(AgentDir))
        {
            try
            {
                Directory.Delete(AgentDir, recursive: true);
            }
            catch
            {
                // No crítico si el directorio está bloqueado transitoriamente
            }
        }
    }
}
