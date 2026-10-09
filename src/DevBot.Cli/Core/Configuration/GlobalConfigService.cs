using System.Text.Json;

namespace DevBot.Cli.Core.Configuration;

/// <summary>
/// Servicio para cargar y persistir la configuración global de usuario en ~/.devbot/config.json.
/// </summary>
public static class GlobalConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetConfigPath(string? customBaseDir = null)
    {
        string baseDir = !string.IsNullOrWhiteSpace(customBaseDir)
            ? customBaseDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".devbot");

        return Path.Combine(baseDir, "config.json");
    }

    /// <summary>
    /// Carga la configuración global desde el disco o devuelve valores predeterminados si no existe.
    /// </summary>
    /// <param name="customBaseDir">Directorio base opcional para pruebas.</param>
    /// <returns>Instancia de <see cref="GlobalConfig"/>.</returns>
    public static GlobalConfig Load(string? customBaseDir = null)
    {
        string path = GetConfigPath(customBaseDir);
        if (!File.Exists(path))
        {
            return new GlobalConfig();
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GlobalConfig>(json, JsonOptions) ?? new GlobalConfig();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal loading global config: {ex.Message}");
            return new GlobalConfig();
        }
    }

    /// <summary>
    /// Guarda la configuración global en el disco.
    /// </summary>
    /// <param name="config">Configuración a almacenar.</param>
    /// <param name="customBaseDir">Directorio base opcional para pruebas.</param>
    public static void Save(GlobalConfig config, string? customBaseDir = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        string path = GetConfigPath(customBaseDir);

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal saving global config: {ex.Message}");
        }
    }
}
