using System.Diagnostics;
using DevBot.Cli.Core.Auditing;
using DevBot.Cli.Core.Interactive;
using DevBot.Cli.Core.Memory;
using DevBot.Cli.Core.Modes;
using DevBot.Cli.Core.Planning;
using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core;

public class AgentPhaseMetric
{
    public string PhaseName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens => PromptTokens + CompletionTokens;
    public Dictionary<string, int> ToolCalls { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Success { get; set; }
    public string? Notes { get; set; }

    public void RecordTool(string toolName)
    {
        if (ToolCalls.TryGetValue(toolName, out int count))
        {
            ToolCalls[toolName] = count + 1;
        }
        else
        {
            ToolCalls[toolName] = 1;
        }
    }
}

public class ExecutionMetrics
{
    private readonly Stopwatch _stopwatch = new();
    private readonly object _lock = new();

    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public int TokensConsumed { get; set; }
    public int CoderRetries { get; set; }
    public bool Success { get; set; }
    public string? CommitHash { get; set; }
    public string? ErrorMessage { get; set; }

    public List<AgentPhaseMetric> Phases { get; } = new();
    public AgentPhaseMetric? CurrentPhase { get; private set; }

    public void Start() => _stopwatch.Start();
    
    public void Stop()
    {
        _stopwatch.Stop();
        lock (_lock)
        {
            if (CurrentPhase != null && CurrentPhase.EndTime == null)
            {
                CurrentPhase.EndTime = DateTime.UtcNow;
            }
        }
    }

    public AgentPhaseMetric StartPhase(string phaseName)
    {
        lock (_lock)
        {
            if (CurrentPhase != null && CurrentPhase.EndTime == null)
            {
                CurrentPhase.EndTime = DateTime.UtcNow;
            }

            var phase = new AgentPhaseMetric
            {
                PhaseName = phaseName,
                StartTime = DateTime.UtcNow
            };
            Phases.Add(phase);
            CurrentPhase = phase;
            return phase;
        }
    }

    public void RecordToolCall(string toolName)
    {
        lock (_lock)
        {
            CurrentPhase?.RecordTool(toolName);
        }
    }

    public void RecordTokens(int promptTokens, int completionTokens)
    {
        lock (_lock)
        {
            int total = promptTokens + completionTokens;
            TokensConsumed += total;
            if (CurrentPhase != null)
            {
                CurrentPhase.PromptTokens += promptTokens;
                CurrentPhase.CompletionTokens += completionTokens;
            }
        }
    }

    public void EndPhase(bool success, string? notes = null)
    {
        lock (_lock)
        {
            if (CurrentPhase != null)
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

public class AgentContext
{
    public string RepoRoot { get; }
    public string AgentDir { get; }
    public string TaskFilePath { get; }
    public string ScoutReportPath { get; }
    public string ReviewResultsPath { get; }
    public string LatestReportPath => Path.Combine(AgentDir, "latest_run_report.md");
    public string ReportsDir => Path.Combine(AgentDir, "reports");

    public string ApiKey { get; }
    public string ModelId { get; set; } = "gemini-3.5-flash-lite";
    public int MaxRetries { get; set; } = 3;

    public string TaskDescription { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public string OriginalBranch { get; set; } = string.Empty;
    public string? PullRequestUrl { get; set; }

    public AgentMode Mode { get; set; } = AgentMode.Feature;
    public ModePolicy ModePolicy { get; set; } = ModePolicyRegistry.GetPolicy(AgentMode.Feature);

    public bool AutoApprove { get; set; } = false;
    public string? AdditionalUserGuidance { get; set; }
    public IHumanFeedbackHandler FeedbackHandler { get; set; } = new ConsoleFeedbackHandler();

    public void SetMode(AgentMode mode)
    {
        Mode = mode;
        ModePolicy = ModePolicyRegistry.GetPolicy(mode);
    }

    public HashSet<string> ModifiedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int? LastCommandExitCode { get; set; }
    public List<(string Command, int ExitCode, bool Success)> CommandExecutionHistory { get; } = new();

    public ProjectStackInfo StackInfo { get; set; } = new(
        ProjectStackType.DotNet, "C#", "dotnet", null, Array.Empty<string>(), Array.Empty<string>());

    public IBuildAndTestStrategy Strategy { get; set; } = new DotNetStrategy();

    public IPreCommitAuditor Auditor { get; set; } = new PreCommitAuditor();

    public string? LocalRules { get; set; }
    public RepoMap? RepoMap { get; set; }
    public IRepositoryMemoryService MemoryService { get; set; } = new RepositoryMemoryService();

    public string PlanJsonPath => Path.Combine(AgentDir, "plan.json");
    public string PlanMarkdownPath => Path.Combine(AgentDir, "plan.md");
    public PlanDefinition? Plan { get; set; }
    public PlanMilestone? CurrentMilestone { get; set; }
    public HashSet<string> MilestoneModifiedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void RecordCommandExecution(string command, int exitCode)
    {
        LastCommandExitCode = exitCode;
        CommandExecutionHistory.Add((command, exitCode, exitCode == 0));
    }

    public ExecutionMetrics Metrics { get; } = new();
    public AgentLogger Logger { get; }

    public AgentContext(string repoRoot, string apiKey, string? modelId = null)
    {
        RepoRoot = Path.GetFullPath(repoRoot);
        AgentDir = Path.Combine(RepoRoot, ".agent");
        TaskFilePath = Path.Combine(AgentDir, "task.md");
        ScoutReportPath = Path.Combine(AgentDir, "scout_report.md");
        ReviewResultsPath = Path.Combine(AgentDir, "review_results.md");

        EnsureAgentDirectory();
        Logger = new AgentLogger(AgentDir);

        ApiKey = apiKey;
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            ModelId = modelId;
        }
    }

    public void EnsureAgentDirectory()
    {
        if (!Directory.Exists(AgentDir))
        {
            Directory.CreateDirectory(AgentDir);
        }
    }

    public void SaveTask(string taskDescription)
    {
        EnsureAgentDirectory();
        TaskDescription = taskDescription;
        File.WriteAllText(TaskFilePath, taskDescription);
    }

    public string ReadTask()
    {
        return File.Exists(TaskFilePath) ? File.ReadAllText(TaskFilePath) : string.Empty;
    }

    public void SaveScoutReport(string report)
    {
        EnsureAgentDirectory();
        File.WriteAllText(ScoutReportPath, report);
    }

    public string ReadScoutReport()
    {
        return File.Exists(ScoutReportPath) ? File.ReadAllText(ScoutReportPath) : string.Empty;
    }

    public void SaveReviewResults(string results)
    {
        EnsureAgentDirectory();
        File.WriteAllText(ReviewResultsPath, results);
    }

    public string ReadReviewResults()
    {
        return File.Exists(ReviewResultsPath) ? File.ReadAllText(ReviewResultsPath) : string.Empty;
    }

    public void SavePlan(PlanDefinition plan)
    {
        EnsureAgentDirectory();
        Plan = plan;
        File.WriteAllText(PlanJsonPath, plan.ToJson());
        File.WriteAllText(PlanMarkdownPath, plan.ToMarkdown());
    }

    public PlanDefinition? ReadPlan()
    {
        if (File.Exists(PlanJsonPath))
        {
            return PlanDefinition.FromJson(File.ReadAllText(PlanJsonPath));
        }
        return null;
    }

    public void CleanAgentArtifacts()
    {
        if (Directory.Exists(AgentDir))
        {
            try
            {
                Directory.Delete(AgentDir, recursive: true);
            }
            catch
            {
                // Ignored if locked temporarily
            }
        }
    }
}
