namespace DevBot.Cli.Core.Strategies;

/// <summary>
/// Información estructural y de compilación detectada automáticamente en un repositorio.
/// </summary>
/// <param name="StackType">Tipo de stack identificado en el proyecto.</param>
/// <param name="Language">Nombre del lenguaje de programación predominante.</param>
/// <param name="BuildTool">Herramienta de compilación o gestor de paquetes principal (ej. "dotnet", "npm", "mvn").</param>
/// <param name="DetectedFile">Ruta al archivo descriptor detectado (ej. solución .sln, pom.xml, package.json).</param>
/// <param name="SourceDirectories">Directorios de código fuente identificados.</param>
/// <param name="TestDirectories">Directorios de pruebas unitarias identificados.</param>
public sealed record ProjectStackInfo(
    ProjectStackType StackType,
    string Language,
    string BuildTool,
    string? DetectedFile,
    IReadOnlyList<string> SourceDirectories,
    IReadOnlyList<string> TestDirectories
);
