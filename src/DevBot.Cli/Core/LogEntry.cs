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
    /// Representación formateada en una sola línea para logs de texto plano.
    /// </summary>
    public override string ToString()
    {
        string localTime = Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
        string agentTag = string.IsNullOrWhiteSpace(AgentName) ? "General" : AgentName;
        string detailStr = Details != null && Details.Count > 0 ? $" | {JsonSerializer.Serialize(Details)}" : "";
        return $"[{localTime}] [{agentTag}] [{EventType}] {Message}{detailStr}";
    }
}
