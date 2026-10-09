namespace DevBot.Cli.Prompts;

/// <summary>
/// Cargador centralizado de plantillas de prompts para los agentes del sistema.
/// <para>
/// <b>Convención de Idioma del Sistema (Decisión Arquitectónica):</b>
/// <list type="bullet">
///   <item>
///     <term>Prompts para LLMs (Inglés):</term>
///     <description>Todos los prompts base enviados a los modelos de lenguaje (Scout, Coder, Reviewer, etc.) 
///     están redactados en inglés para optimizar la adherencia a instrucciones, determinismo y razonamiento
///     técnico a través de múltiples proveedores (Gemini, OpenAI, Anthropic).</description>
///   </item>
///   <item>
///     <term>Consola y UX de Usuario (Español):</term>
///     <description>La interfaz de línea de comandos, mensajes de diagnóstico, tablas de Spectre.Console,
///     logs de usuario y manuales de ayuda se expresan en español para la experiencia del desarrollador.</description>
///   </item>
/// </list>
/// </para>
/// </summary>
public static class PromptLoader
{
    /// <summary>
    /// Carga el contenido de un prompt desde el sistema de archivos buscando en ubicaciones candidatas,
    /// recurriendo al contenido predeterminado en caso de error o ausencia.
    /// </summary>
    /// <param name="promptFileName">Nombre del archivo de plantilla de prompt (ej. "scout_system.txt").</param>
    /// <param name="fallbackContent">Contenido embebido de respaldo.</param>
    /// <returns>Texto del prompt listo para su uso.</returns>
    public static string LoadPrompt(string promptFileName, string fallbackContent)
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string path1 = Path.Combine(baseDir, "Prompts", promptFileName);
            if (File.Exists(path1)) return File.ReadAllText(path1);

            string path2 = Path.Combine(baseDir, promptFileName);
            if (File.Exists(path2)) return File.ReadAllText(path2);

            string currentDir = Directory.GetCurrentDirectory();
            string path3 = Path.Combine(currentDir, "src", "DevBot.Cli", "Prompts", promptFileName);
            if (File.Exists(path3)) return File.ReadAllText(path3);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal loading prompt '{promptFileName}': {ex.Message}");
        }

        return fallbackContent;
    }
}
