using DevBot.Cli.Core.Modes;

namespace DevBot.Cli.Core.Configuration;

/// <summary>
/// Opciones y parámetros de ejecución fuertemente tipados parseados desde los argumentos de la línea de comandos.
/// </summary>
public sealed record CliOptions
{
    public string? TaskDescription { get; init; }
    public string RepoDir { get; init; } = Directory.GetCurrentDirectory();
    public string? ApiKey { get; init; }
    public string ModelId { get; init; } = "gemini-3.5-flash-lite";
    public int MaxRetries { get; init; } = 3;
    public bool AutoApprove { get; init; }
    public string? ModeString { get; init; }
    public AgentMode? ExplicitMode { get; init; }

    public bool ShowHelp { get; init; }
    public bool ShowReport { get; init; }
    public bool ShowLogs { get; init; }
}
