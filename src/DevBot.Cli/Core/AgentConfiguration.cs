namespace DevBot.Cli.Core;

/// <summary>
/// Configuración inmutable de ejecución para una sesión de DevBot.
/// Contiene parámetros de conexión al LLM y políticas de reintentos.
/// Responsabilidad única: almacenar la configuración estática que no muta durante la ejecución.
/// </summary>
public sealed record AgentConfiguration
{
    /// <summary>Clave API para la conexión con el modelo de lenguaje.</summary>
    public string ApiKey { get; init; }

    /// <summary>Identificador del modelo de lenguaje a utilizar.</summary>
    public string ModelId { get; init; }

    /// <summary>Máximo de ciclos de corrección Coder-Reviewer por hito.</summary>
    public int MaxRetries { get; init; }

    /// <summary>Indica si se omiten las confirmaciones interactivas (flag --yes).</summary>
    public bool AutoApprove { get; init; }

    /// <summary>
    /// Crea una nueva configuración inmutable de ejecución.
    /// </summary>
    /// <param name="apiKey">Clave API requerida (no puede estar vacía).</param>
    /// <param name="modelId">Identificador del modelo LLM (por defecto gemini-3.5-flash-lite).</param>
    /// <param name="maxRetries">Máximo de reintentos Coder-Reviewer (por defecto 3).</param>
    /// <param name="autoApprove">Aprobación automática sin confirmación interactiva.</param>
    public AgentConfiguration(
        string apiKey,
        string modelId = "gemini-3.5-flash-lite",
        int maxRetries = 3,
        bool autoApprove = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        ApiKey = apiKey;
        ModelId = !string.IsNullOrWhiteSpace(modelId) ? modelId : "gemini-3.5-flash-lite";
        MaxRetries = maxRetries > 0 ? maxRetries : 3;
        AutoApprove = autoApprove;
    }
}
