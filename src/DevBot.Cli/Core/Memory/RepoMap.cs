namespace DevBot.Cli.Core.Memory;

/// <summary>
/// Mapa estructurado de la arquitectura de un repositorio de código.
/// Se serializa en disco (.devbot/repo_map.json) y se indexa por commit hash para acelerar la exploración agéntica.
/// </summary>
public sealed class RepoMap
{
    /// <summary>
    /// Hash del commit Git sobre el cual se generó este mapa arquitectónico.
    /// </summary>
    public string LastCommitHash { get; set; } = string.Empty;

    /// <summary>
    /// Marca de tiempo UTC del último escaneo arquitectónico.
    /// </summary>
    public DateTime LastScannedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Lenguaje de programación principal detectado en el repositorio.
    /// </summary>
    public string PrimaryLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Ruta al archivo principal de build o solución (ej. "MiApp.sln", "package.json").
    /// </summary>
    public string? BuildFilePath { get; set; }

    /// <summary>
    /// Lista de archivos punto de entrada identificados (ej. "Program.cs", "index.ts", "main.py").
    /// </summary>
    public List<string> Entrypoints { get; set; } = [];

    /// <summary>
    /// Capas principales identificadas en la solución.
    /// </summary>
    public List<string> MainLayers { get; set; } = [];

    /// <summary>
    /// Desglose de componentes arquitectónicos registrados en el mapa.
    /// </summary>
    public List<RepoComponentInfo> Components { get; set; } = [];
}
