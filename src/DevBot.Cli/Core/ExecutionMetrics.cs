using System.Diagnostics;

namespace DevBot.Cli.Core;

/// <summary>
/// Gestor thread-safe de telemetría y métricas globales de una misión de desarrollo autónomo.
/// Proporciona cronometraje de alta precisión, agregación de consumo de tokens y ciclo de vida de fases.
/// </summary>
public sealed class ExecutionMetrics
{
    private readonly Stopwatch _stopwatch = new();
    private readonly object _lock = new();
    private readonly List<AgentPhaseMetric> _phases = [];

    /// <summary>
    /// Tiempo de ejecución transcurrido registrado por el cronómetro de alta precisión.
    /// </summary>
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    /// <summary>
    /// Total acumulado de tokens consumidos a lo largo de toda la misión.
    /// </summary>
    public int TokensConsumed { get; set; }

    /// <summary>
    /// Cantidad de reintentos ejecutados en el ciclo de corrección Coder-Reviewer.
    /// </summary>
    public int CoderRetries { get; set; }

    /// <summary>
    /// Indica si la misión global concluyó satisfactoriamente.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Hash del commit Git resultante o checkpoint final alcanzado.
    /// </summary>
    public string? CommitHash { get; set; }

    /// <summary>
    /// Mensaje descriptivo en caso de aborto o fallo irrecuperable.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Devuelve una lista de las fases ejecutadas. 
    /// Retorna una copia defensiva para prevenir excepciones concurrentes durante enumeraciones.
    /// </summary>
    public List<AgentPhaseMetric> Phases
    {
        get
        {
            lock (_lock)
            {
                return [.. _phases];
            }
        }
    }

    /// <summary>
    /// Fase activa actual bajo ejecución, o null si ninguna está en curso.
    /// </summary>
    public AgentPhaseMetric? CurrentPhase { get; private set; }

    /// <summary>
    /// Inicia o reanuda el cronómetro de ejecución global.
    /// </summary>
    public void Start() => _stopwatch.Start();

    /// <summary>
    /// Detiene el cronómetro global y finaliza formalmente la fase activa si aún permanecía abierta.
    /// </summary>
    public void Stop()
    {
        _stopwatch.Stop();
        lock (_lock)
        {
            if (CurrentPhase is { EndTime: null })
            {
                CurrentPhase.EndTime = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Inicia una nueva fase de ejecución, cerrando automáticamente la anterior si no fue finalizada explícitamente.
    /// </summary>
    /// <param name="phaseName">Nombre descriptivo de la fase a iniciar.</param>
    /// <returns>La instancia de métrica de la nueva fase creada.</returns>
    public AgentPhaseMetric StartPhase(string phaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phaseName);

        lock (_lock)
        {
            if (CurrentPhase is { EndTime: null })
            {
                CurrentPhase.EndTime = DateTime.UtcNow;
            }

            var phase = new AgentPhaseMetric
            {
                PhaseName = phaseName,
                StartTime = DateTime.UtcNow
            };

            _phases.Add(phase);
            CurrentPhase = phase;
            return phase;
        }
    }

    /// <summary>
    /// Registra la invocación a una herramienta dentro de la fase activa actual.
    /// </summary>
    /// <param name="toolName">Nombre de la herramienta ejecutada.</param>
    public void RecordToolCall(string toolName)
    {
        lock (_lock)
        {
            CurrentPhase?.RecordTool(toolName);
        }
    }

    /// <summary>
    /// Acumula tokens consumidos tanto en el consolidado global como en la fase activa actual.
    /// </summary>
    /// <param name="promptTokens">Tokens consumidos en el prompt de entrada.</param>
    /// <param name="completionTokens">Tokens generados en la respuesta.</param>
    public void RecordTokens(int promptTokens, int completionTokens)
    {
        lock (_lock)
        {
            int total = promptTokens + completionTokens;
            TokensConsumed += total;

            if (CurrentPhase is not null)
            {
                CurrentPhase.PromptTokens += promptTokens;
                CurrentPhase.CompletionTokens += completionTokens;
            }
        }
    }

    /// <summary>
    /// Cierra y registra el resultado final de la fase activa actual.
    /// </summary>
    /// <param name="success">Indica si la fase cumplió su objetivo.</param>
    /// <param name="notes">Notas explicativas o causas de fallo opcionales.</param>
    public void EndPhase(bool success, string? notes = null)
    {
        lock (_lock)
        {
            if (CurrentPhase is not null)
            {
                CurrentPhase.EndTime = DateTime.UtcNow;
                CurrentPhase.Success = success;
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    CurrentPhase.Notes = notes;
                }
            }
        }
    }
}
