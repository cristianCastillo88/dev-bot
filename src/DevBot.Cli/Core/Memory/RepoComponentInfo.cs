namespace DevBot.Cli.Core.Memory;

/// <summary>
/// Modela la metadata arquitectónica de un componente o módulo relevante detectado en el repositorio.
/// </summary>
public sealed class RepoComponentInfo
{
    /// <summary>
    /// Nombre descriptivo del componente (ej. "Core", "Controllers", "Services").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Ruta relativa dentro del repositorio.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Capa arquitectónica asociada (ej. "Domain", "Application", "Infrastructure", "Presentation").
    /// </summary>
    public string Layer { get; set; } = string.Empty;

    /// <summary>
    /// Tipos, clases o interfaces clave que forman parte de este componente.
    /// </summary>
    public List<string> KeyTypes { get; set; } = [];
}
