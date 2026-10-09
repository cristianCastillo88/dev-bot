using System.Text.Json;

namespace DevBot.Cli.Core;

/// <summary>
/// Modela una entrada de auditoría individual registrada por el sistema de logging de DevBot.
/// </summary>
public sealed class LogEntry
{
    /// <summary>
    /// Marca de tiempo UTC en que ocurrió el evento.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Nombre del agente o subsistema emisor (ej. "Orchestrator", "ScoutAgent", "CoderAgent").
    /// </summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>
    /// Tipo de evento registrado (ej. "PhaseStart", "PhaseEnd", "ToolCall", "ToolResult", "Warning", "Error").
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Mensaje descriptivo de la acción o suceso.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Diccionario opcional con metadatos estructurados adicionales del evento.
    /// </summary>
    public Dictionary<string, object>? Details { get; set; }

    /// <summary>
    /// Hash criptográfico de la entrada precedente en la cadena de auditoría (hash chain).
    /// </summary>
    public string PreviousHash { get; set; } = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// Hash SHA-256 de la entrada actual que garantiza no-repudio e inmutabilidad.
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Calcula el hash SHA-256 de los campos de la entrada encadenados a PreviousHash.
    /// </summary>
    public string ComputeHash()
    {
        string raw = $"{PreviousHash}|{Timestamp:O}|{AgentName}|{EventType}|{Message}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Representación formateada en una sola línea para logs de texto plano.
    /// </summary>
    public override string ToString()
    {
        string localTime = Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
        string agentTag = string.IsNullOrWhiteSpace(AgentName) ? "General" : AgentName;
        string detailStr = Details != null && Details.Count > 0 ? $" | {JsonSerializer.Serialize(Details)}" : "";
        string hashSnippet = !string.IsNullOrEmpty(Hash) && Hash.Length >= 8 ? $" [hash:{Hash[..8]}]" : "";
        return $"[{localTime}] [{agentTag}] [{EventType}]{hashSnippet} {Message}{detailStr}";
    }
}
