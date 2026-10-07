using DevBot.Cli.Core.Auditing;
using DevBot.Cli.Core.Interactive;
using DevBot.Cli.Core.Memory;
using DevBot.Cli.Core.Modes;
using DevBot.Cli.Core.Planning;
using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core;

/// <summary>
/// Bus de estado mutable compartido para el ciclo de vida de una ejecución en DevBot.
/// Compone <see cref="AgentWorkspace"/> (rutas) y <see cref="AgentConfiguration"/> (config inmutable),
/// y gestiona el estado mutable de ejecución: plan, hitos, métricas, stack detectado, etc.
/// </summary>
public class AgentContext
{
    // =========================================================================
    // Objetos Componentes (SRP: cada uno encapsula una responsabilidad distinta)
    // =========================================================================

    /// <summary>Gestión de rutas y directorio de artefactos del workspace.</summary>
    public AgentWorkspace Workspace { get; }

    /// <summary>Configuración inmutable de la sesión (API key, modelo, reintentos).</summary>
    public AgentConfiguration Config { get; private set; }

    // =========================================================================
    // Propiedades de Forwarding (Retrocompatibilidad con callers existentes)
    // =========================================================================

    /// <summary>Ruta raíz del repositorio de destino.</summary>
    public string RepoRoot => Workspace.RepoRoot;

    /// <summary>Directorio transitorio de artefactos .agent.</summary>
    public string AgentDir => Workspace.AgentDir;

    /// <summary>Ruta del archivo de descripción de tarea.</summary>
    public string TaskFilePath => Workspace.TaskFilePath;

    /// <summary>Ruta del reporte de exploración Scout.</summary>
    public string ScoutReportPath => Workspace.ScoutReportPath;

    /// <summary>Ruta de los resultados de verificación del Reviewer.</summary>
    public string ReviewResultsPath => Workspace.ReviewResultsPath;

    /// <summary>Ruta del último informe ejecutivo generado.</summary>
    public string LatestReportPath => Workspace.LatestReportPath;

    /// <summary>Directorio de reportes históricos.</summary>
    public string ReportsDir => Workspace.ReportsDir;

    /// <summary>Ruta del plan de hitos en formato JSON.</summary>
    public string PlanJsonPath => Workspace.PlanJsonPath;

    /// <summary>Ruta del plan de hitos en formato Markdown.</summary>
    public string PlanMarkdownPath => Workspace.PlanMarkdownPath;

    /// <summary>Clave API para la conexión con el modelo de lenguaje.</summary>
    public string ApiKey => Config.ApiKey;

    /// <summary>Identificador del modelo de lenguaje.</summary>
    public string ModelId
    {
        get => Config.ModelId;
        set => Config = Config with { ModelId = value };
    }

    /// <summary>Máximo de ciclos de corrección Coder-Reviewer.</summary>
    public int MaxRetries
    {
        get => Config.MaxRetries;
        set => Config = Config with { MaxRetries = value };
    }

    /// <summary>Indica si se omiten las confirmaciones interactivas.</summary>
    public bool AutoApprove
    {
        get => Config.AutoApprove;
        set => Config = Config with { AutoApprove = value };
    }

    // =========================================================================
    // Estado Mutable de Ejecución
    // =========================================================================

    /// <summary>Descripción textual de la tarea asignada.</summary>
    public string TaskDescription { get; set; } = string.Empty;

    /// <summary>Rama Git de trabajo creada para la misión.</summary>
    public string TargetBranch { get; set; } = string.Empty;

    /// <summary>Rama Git original del repositorio antes de la ejecución.</summary>
    public string OriginalBranch { get; set; } = string.Empty;

    /// <summary>URL del Pull Request generado (si aplica).</summary>
    public string? PullRequestUrl { get; set; }

    // Política y Modo de Operación
    /// <summary>Modo operacional activo.</summary>
    public AgentMode Mode { get; private set; } = AgentMode.Feature;

    /// <summary>Política de restricciones asociada al modo activo.</summary>
    public ModePolicy ModePolicy { get; private set; } = ModePolicyRegistry.GetPolicy(AgentMode.Feature);

    // Human-in-the-Loop y Retroalimentación
    /// <summary>Directivas adicionales inyectadas por el desarrollador.</summary>
    public string? AdditionalUserGuidance { get; set; }

    /// <summary>Handler para interacción Human-in-the-Loop.</summary>
    public IHumanFeedbackHandler FeedbackHandler { get; set; } = new ConsoleFeedbackHandler();

    // Rastreabilidad de Modificaciones y Comandos
    /// <summary>Conjunto de archivos modificados durante la ejecución.</summary>
    public HashSet<string> ModifiedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Código de salida del último comando ejecutado.</summary>
    public int? LastCommandExitCode { get; set; }

    /// <summary>Historial de ejecución de comandos de terminal.</summary>
    public List<(string Command, int ExitCode, bool Success)> CommandExecutionHistory { get; } = [];

    // Estrategia Políglota y Stack Detectado
    /// <summary>Información del stack tecnológico detectado.</summary>
    public ProjectStackInfo StackInfo { get; set; } = new(
        ProjectStackType.DotNet, "C#", "dotnet", null, [], []);

    /// <summary>Estrategia de build y test para el stack detectado.</summary>
    public IBuildAndTestStrategy Strategy { get; set; } = new DotNetStrategy();

    // Auditoría de Seguridad y Calidad
    /// <summary>Auditor de seguridad pre-commit.</summary>
    public IPreCommitAuditor Auditor { get; set; } = new PreCommitAuditor();

    // Memoria y Reglas de Repositorio (.devbotrules)
    /// <summary>Reglas locales del equipo cargadas desde .devbotrules o devbot.json.</summary>
    public string? LocalRules { get; set; }

    /// <summary>Mapa de arquitectura del repositorio.</summary>
    public RepoMap? RepoMap { get; set; }

    /// <summary>Servicio de memoria persistente del repositorio.</summary>
    public IRepositoryMemoryService MemoryService { get; set; } = new RepositoryMemoryService();

    // Descomposición en Hitos
    /// <summary>Plan de ejecución con hitos atómicos.</summary>
    public PlanDefinition? Plan { get; set; }

    /// <summary>Hito actualmente en ejecución.</summary>
    public PlanMilestone? CurrentMilestone { get; set; }

    /// <summary>Archivos modificados dentro del hito actual.</summary>
    public HashSet<string> MilestoneModifiedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Métricas y Registro Estructurado
    /// <summary>Métricas de telemetría de la ejecución.</summary>
    public ExecutionMetrics Metrics { get; } = new();

    /// <summary>Logger estructurado para auditoría.</summary>
    public AgentLogger Logger { get; }

    // =========================================================================
    // Constructores
    // =========================================================================

    /// <summary>
    /// Constructor principal que inicializa el contexto componiendo workspace y configuración.
    /// </summary>
    /// <param name="repoRoot">Ruta raíz del repositorio de destino.</param>
    /// <param name="apiKey">Clave API para la conexión con el modelo de lenguaje.</param>
    /// <param name="modelId">Identificador del modelo de lenguaje (por defecto gemini-3.5-flash-lite).</param>
    public AgentContext(string repoRoot, string apiKey, string? modelId = null)
    {
        Workspace = new AgentWorkspace(repoRoot);
        Config = new AgentConfiguration(
            apiKey,
            modelId ?? "gemini-3.5-flash-lite"
        );
        Logger = new AgentLogger(Workspace.AgentDir);
    }

    /// <summary>
    /// Constructor avanzado que recibe objetos componentes pre-construidos.
    /// </summary>
    /// <param name="workspace">Workspace con rutas pre-configuradas.</param>
    /// <param name="config">Configuración inmutable de la sesión.</param>
    public AgentContext(AgentWorkspace workspace, AgentConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(config);

        Workspace = workspace;
        Config = config;
        Logger = new AgentLogger(Workspace.AgentDir);
    }

    // =========================================================================
    // Métodos de Estado
    // =========================================================================

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
    public void EnsureAgentDirectory() => Workspace.EnsureAgentDirectory();

    // =========================================================================
    // Métodos Asíncronos de Persistencia (Modern .NET 8 con CancellationToken)
    // =========================================================================

    public async Task SaveTaskAsync(string taskDescription, CancellationToken ct = default)
    {
        Workspace.EnsureAgentDirectory();
        TaskDescription = taskDescription;
        await File.WriteAllTextAsync(Workspace.TaskFilePath, taskDescription, ct);
    }

    public async Task<string> ReadTaskAsync(CancellationToken ct = default)
    {
        return File.Exists(Workspace.TaskFilePath) ? await File.ReadAllTextAsync(Workspace.TaskFilePath, ct) : string.Empty;
    }

    public async Task SaveScoutReportAsync(string report, CancellationToken ct = default)
    {
        Workspace.EnsureAgentDirectory();
        await File.WriteAllTextAsync(Workspace.ScoutReportPath, report, ct);
    }

    public async Task<string> ReadScoutReportAsync(CancellationToken ct = default)
    {
        return File.Exists(Workspace.ScoutReportPath) ? await File.ReadAllTextAsync(Workspace.ScoutReportPath, ct) : string.Empty;
    }

    public async Task SaveReviewResultsAsync(string results, CancellationToken ct = default)
    {
        Workspace.EnsureAgentDirectory();
        await File.WriteAllTextAsync(Workspace.ReviewResultsPath, results, ct);
    }

    public async Task<string> ReadReviewResultsAsync(CancellationToken ct = default)
    {
        return File.Exists(Workspace.ReviewResultsPath) ? await File.ReadAllTextAsync(Workspace.ReviewResultsPath, ct) : string.Empty;
    }

    public async Task SavePlanAsync(PlanDefinition plan, CancellationToken ct = default)
    {
        Workspace.EnsureAgentDirectory();
        Plan = plan;
        await File.WriteAllTextAsync(Workspace.PlanJsonPath, plan.ToJson(), ct);
        await File.WriteAllTextAsync(Workspace.PlanMarkdownPath, plan.ToMarkdown(), ct);
    }

    public async Task<PlanDefinition?> ReadPlanAsync(CancellationToken ct = default)
    {
        if (File.Exists(Workspace.PlanJsonPath))
        {
            string json = await File.ReadAllTextAsync(Workspace.PlanJsonPath, ct);
            return PlanDefinition.FromJson(json);
        }
        return null;
    }

    /// <summary>
    /// Limpia los artefactos temporales eliminando el directorio .agent si existe.
    /// </summary>
    public void CleanAgentArtifacts() => Workspace.CleanAgentArtifacts();
}
