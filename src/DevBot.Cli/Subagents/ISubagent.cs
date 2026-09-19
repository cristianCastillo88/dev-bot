using DevBot.Cli.Core;

namespace DevBot.Cli.Subagents;

public interface ISubagent
{
    string Name { get; }
    Task<bool> ExecuteAsync(AgentContext context, CancellationToken cancellationToken = default);
}
