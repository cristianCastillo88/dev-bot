using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevBot.Cli.Core;

/// <summary>
/// Registrador estructurado de eventos y telemetría de DevBot.
/// Escribe simultáneamente en archivos de texto plano (.log) y formatos estructurados (.json) bajo el directorio .agent/logs/.
/// </summary>
public sealed class AgentLogger
{
    private readonly string _logsDir;
    private readonly string _latestLogPath;
    private readonly string _sessionLogPath;
    private readonly string _latestJsonPath;
    private readonly string _sessionJsonPath;

    private readonly List<LogEntry> _entries = [];
    private readonly object _lock = new();
    private string _lastHash = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>
    /// Inicializa una nueva sesión de logging dentro del directorio .agent especificado.
    /// </summary>
    /// <param name="agentDir">Ruta al directorio .agent en la raíz del repositorio.</param>
    public AgentLogger(string agentDir)
    {
        _logsDir = Path.Combine(agentDir, "logs");
        if (!Directory.Exists(_logsDir))
        {
            Directory.CreateDirectory(_logsDir);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _latestLogPath = Path.Combine(_logsDir, "latest.log");
        _sessionLogPath = Path.Combine(_logsDir, $"devbot_{timestamp}.log");
        _latestJsonPath = Path.Combine(_logsDir, "latest.json");
        _sessionJsonPath = Path.Combine(_logsDir, $"devbot_{timestamp}.json");

        // Inicializa latest.log para la nueva corrida
        try
        {
            File.WriteAllText(_latestLogPath, $"=== DevBot Execution Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal initializing latest.log: {ex.Message}");
        }
    }

    /// <summary>
    /// Ruta física al archivo latest.log de la sesión activa.
    /// </summary>
    public string LatestLogPath => _latestLogPath;

    /// <summary>
    /// Ruta física al archivo latest.json de la sesión activa.
    /// </summary>
    public string LatestJsonPath => _latestJsonPath;

    /// <summary>
    /// Registra un evento de auditoría de forma atómica y thread-safe con firma criptográfica encadenada.
    /// </summary>
    /// <param name="agentName">Nombre del emisor del evento.</param>
    /// <param name="eventType">Categoría del evento.</param>
    /// <param name="message">Descripción del evento.</param>
    /// <param name="details">Metadatos estructurados opcionales.</param>
    public void LogEvent(string agentName, string eventType, string message, Dictionary<string, object>? details = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            AgentName = agentName,
            EventType = eventType,
            Message = message,
            Details = details
        };

        lock (_lock)
        {
            entry.PreviousHash = _lastHash;
            entry.Hash = entry.ComputeHash();
            _lastHash = entry.Hash;

            _entries.Add(entry);
            string line = entry.ToString() + Environment.NewLine;

            try
            {
                File.AppendAllText(_latestLogPath, line, Encoding.UTF8);
                File.AppendAllText(_sessionLogPath, line, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal appending to agent log: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Obtiene una instantánea de solo lectura de todas las entradas registradas hasta el momento.
    /// </summary>
    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
        {
            return [.. _entries];
        }
    }

    /// <summary>
    /// Valida la integridad criptográfica de la sesión actual de logging.
    /// </summary>
    public (bool IsValid, string? ErrorMessage) VerifyCurrentSessionIntegrity()
    {
        lock (_lock)
        {
            return VerifyIntegrity(_entries);
        }
    }

    /// <summary>
    /// Valida la integridad criptográfica de una lista de entradas de auditoría (hash chain).
    /// </summary>
    public static (bool IsValid, string? ErrorMessage) VerifyIntegrity(IReadOnlyList<LogEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return (true, null);
        }

        string expectedPrev = "0000000000000000000000000000000000000000000000000000000000000000";
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!string.Equals(entry.PreviousHash, expectedPrev, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Discrepancia en PreviousHash en entrada #{i}: esperaba '{expectedPrev}' pero encontró '{entry.PreviousHash}'.");
            }

            string expectedHash = entry.ComputeHash();
            if (!string.Equals(entry.Hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"Hash inválido en entrada #{i} ('{entry.EventType}'): contenido alterado.");
            }

            expectedPrev = entry.Hash;
        }

        return (true, null);
    }

    /// <summary>
    /// Exporta las entradas acumuladas en formato JSON estructurado indentado.
    /// </summary>
    public void SaveJson()
    {
        lock (_lock)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                string json = JsonSerializer.Serialize(_entries, options);
                File.WriteAllText(_latestJsonPath, json, Encoding.UTF8);
                File.WriteAllText(_sessionJsonPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal serializing agent log to JSON: {ex.Message}");
            }
        }
    }
}
