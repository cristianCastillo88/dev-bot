namespace DevBot.Cli.Core.Configuration;

/// <summary>
/// Modelo fuertemente tipado para las preferencias globales almacenadas en ~/.devbot/config.json.
/// </summary>
public sealed class GlobalConfig
{
    public string DefaultModel { get; set; } = "gemini-3.5-flash-lite";
    public string DefaultProvider { get; set; } = "gemini";
    public int MaxRetries { get; set; } = 3;
    public bool AutoApprove { get; set; } = false;
    public string Language { get; set; } = "es";
}
