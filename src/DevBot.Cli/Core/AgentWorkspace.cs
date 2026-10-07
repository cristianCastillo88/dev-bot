namespace DevBot.Cli.Core;

/// <summary>
/// Encapsula las rutas de trabajo y gestión del directorio de artefactos transitorio (.agent)
/// dentro de un repositorio objetivo. Responsabilidad única: localización y creación de paths.
/// </summary>
public sealed class AgentWorkspace
{
    /// <summary>
    /// Ruta raíz absoluta del repositorio de destino.
    /// </summary>
    public string RepoRoot { get; }

    /// <summary>
    /// Directorio transitorio donde DevBot persiste artefactos de ejecución.
    /// </summary>
    public string AgentDir { get; }

    /// <summary>Ruta del archivo de descripción de tarea.</summary>
    public string TaskFilePath { get; }

    /// <summary>Ruta del reporte de exploración Scout.</summary>
    public string ScoutReportPath { get; }

    /// <summary>Ruta de los resultados de verificación del Reviewer.</summary>
    public string ReviewResultsPath { get; }

    /// <summary>Ruta del último informe ejecutivo generado.</summary>
    public string LatestReportPath => Path.Combine(AgentDir, "latest_run_report.md");

    /// <summary>Directorio de reportes históricos.</summary>
    public string ReportsDir => Path.Combine(AgentDir, "reports");

    /// <summary>Ruta del plan de hitos en formato JSON.</summary>
    public string PlanJsonPath => Path.Combine(AgentDir, "plan.json");

    /// <summary>Ruta del plan de hitos en formato Markdown.</summary>
    public string PlanMarkdownPath => Path.Combine(AgentDir, "plan.md");

    /// <summary>
    /// Inicializa el workspace a partir de la ruta raíz del repositorio.
    /// Crea el directorio .agent si no existe.
    /// </summary>
    /// <param name="repoRoot">Ruta raíz del repositorio de destino.</param>
    public AgentWorkspace(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        RepoRoot = Path.GetFullPath(repoRoot);
        AgentDir = Path.Combine(RepoRoot, ".agent");
        TaskFilePath = Path.Combine(AgentDir, "task.md");
        ScoutReportPath = Path.Combine(AgentDir, "scout_report.md");
        ReviewResultsPath = Path.Combine(AgentDir, "review_results.md");

        EnsureAgentDirectory();
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal: Could not clean agent artifacts: {ex.Message}");
            }
        }
    }
}
