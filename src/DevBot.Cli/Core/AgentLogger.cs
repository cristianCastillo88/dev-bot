using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevBot.Cli.Core;

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string AgentName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty; // PhaseStart, PhaseEnd, ToolCall, ToolResult, ModelResponse, GitAction, Warning, Error
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object>? Details { get; set; }

    public override string ToString()
    {
        string localTime = Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
        string agentTag = string.IsNullOrWhiteSpace(AgentName) ? "General" : AgentName;
        string detailStr = Details != null && Details.Count > 0 ? $" | {JsonSerializer.Serialize(Details)}" : "";
        return $"[{localTime}] [{agentTag}] [{EventType}] {Message}{detailStr}";
    }
}

public class AgentLogger
{
    private readonly string _logsDir;
    private readonly string _latestLogPath;
    private readonly string _sessionLogPath;
    private readonly string _latestJsonPath;
    private readonly string _sessionJsonPath;

    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

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

        // Clear or initialize latest.log for the new run
        try
        {
            File.WriteAllText(_latestLogPath, $"=== DevBot Execution Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
            // Non-fatal if initial write fails
        }
    }

    public string LatestLogPath => _latestLogPath;
    public string LatestJsonPath => _latestJsonPath;

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
            _entries.Add(entry);
            string line = entry.ToString() + Environment.NewLine;

            try
            {
                File.AppendAllText(_latestLogPath, line, Encoding.UTF8);
                File.AppendAllText(_sessionLogPath, line, Encoding.UTF8);
            }
            catch
            {
                // Non-fatal if append fails
            }
        }
    }

    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

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
            catch
            {
                // Non-fatal if json save fails
            }
        }
    }
}
