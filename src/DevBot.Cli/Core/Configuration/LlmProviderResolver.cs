namespace DevBot.Cli.Core.Configuration;

/// <summary>
/// Resuelve endpoints, modelos y proveedores de LLM compatibles con el protocolo OpenAI Chat Completion.
/// </summary>
public static class LlmProviderResolver
{
    public const string DefaultGeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/";
    public const string DefaultOpenAiEndpoint = "https://api.openai.com/v1/";
    public const string DefaultAnthropicEndpoint = "https://api.anthropic.com/v1/";

    /// <summary>
    /// Resuelve el endpoint URI, modelo por defecto y nombre normalizado del proveedor según flags y API key.
    /// </summary>
    /// <param name="providerFlag">Flag explícito --provider (opcional).</param>
    /// <param name="apiKey">Clave API del usuario.</param>
    /// <param name="requestedModel">Identificador de modelo solicitado (opcional).</param>
    /// <returns>Tupla con Endpoint, ModelId y nombre del Provider normalizado.</returns>
    public static (Uri Endpoint, string ModelId, string Provider) Resolve(
        string? providerFlag,
        string apiKey,
        string? requestedModel)
    {
        string provider = !string.IsNullOrWhiteSpace(providerFlag)
            ? providerFlag.Trim().ToLowerInvariant()
            : DetectProviderByApiKey(apiKey);

        return provider switch
        {
            "openai" => (
                new Uri(DefaultOpenAiEndpoint),
                !string.IsNullOrWhiteSpace(requestedModel) && !requestedModel.Contains("gemini", StringComparison.OrdinalIgnoreCase)
                    ? requestedModel
                    : "gpt-4o-mini",
                "openai"
            ),
            "anthropic" => (
                new Uri(DefaultAnthropicEndpoint),
                !string.IsNullOrWhiteSpace(requestedModel) && !requestedModel.Contains("gemini", StringComparison.OrdinalIgnoreCase)
                    ? requestedModel
                    : "claude-3-5-sonnet-20241022",
                "anthropic"
            ),
            _ => (
                new Uri(DefaultGeminiEndpoint),
                !string.IsNullOrWhiteSpace(requestedModel)
                    ? requestedModel
                    : "gemini-3.5-flash-lite",
                "gemini"
            )
        };
    }

    /// <summary>
    /// Infiere automáticamente el proveedor LLM según el prefijo de la clave API.
    /// </summary>
    /// <param name="apiKey">Clave API.</param>
    /// <returns>"openai", "anthropic" o "gemini" como fallback.</returns>
    public static string DetectProviderByApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return "gemini";

        string trimmed = apiKey.Trim();
        if (trimmed.StartsWith("sk-ant-", StringComparison.OrdinalIgnoreCase))
        {
            return "anthropic";
        }

        if (trimmed.StartsWith("sk-proj-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
        {
            return "openai";
        }

        return "gemini";
    }
}
