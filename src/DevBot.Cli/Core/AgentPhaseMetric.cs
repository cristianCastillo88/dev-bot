namespace DevBot.Cli.Core;

/// <summary>
/// Representa la telemetría atómica y las métricas de rendimiento registradas durante la ejecución 
/// de una fase o subagente específico dentro del ciclo agéntico.
/// </summary>
public sealed class AgentPhaseMetric
{
    private readonly object _syncRoot = new();

    /// <summary>
    /// Nombre identificador de la fase o subagente (ej. "Scout Agent", "Coder (Hito 1)").
    /// </summary>
    public string PhaseName { get; init; } = string.Empty;

    /// <summary>
    /// Marca de tiempo UTC en que inició la fase.
    /// </summary>
    public DateTime StartTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Marca de tiempo UTC en que concluyó la fase, o null si aún continúa activa.
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Duración total transcurrida de la fase.
    /// </summary>
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;

    /// <summary>
    /// Cantidad de tokens consumidos en prompts hacia el modelo de lenguaje durante la fase.
    /// </summary>
    public int PromptTokens { get; set; }

    /// <summary>
    /// Cantidad de tokens emitidos en respuestas por el modelo de lenguaje durante la fase.
    /// </summary>
    public int CompletionTokens { get; set; }

    /// <summary>
    /// Cantidad total consolidada de tokens consumidos (Prompt + Completion).
    /// </summary>
    public int TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>
    /// Diccionario con el conteo de invocaciones a herramientas nativas efectuadas en la fase.
    /// </summary>
    public Dictionary<string, int> ToolCalls { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Indica si la fase concluyó exitosamente sin fallos no recuperados.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Notas u observaciones diagnósticas asociadas al cierre de la fase.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Registra una llamada a una herramienta asegurando atomicidad ante accesos concurrentes.
    /// </summary>
    /// <param name="toolName">Nombre de la herramienta invocada (ej. "ReadFile", "RunCommand").</param>
    public void RecordTool(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        lock (_syncRoot)
        {
            ToolCalls[toolName] = ToolCalls.TryGetValue(toolName, out int count) ? count + 1 : 1;
        }
    }
}
