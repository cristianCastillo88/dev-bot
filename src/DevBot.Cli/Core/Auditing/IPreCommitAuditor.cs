using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Tools;

namespace DevBot.Cli.Core.Auditing;

public interface IPreCommitAuditor
{
    Task<AuditResult> AuditAsync(
        string repoRoot,
        IEnumerable<string> modifiedFiles,
        IBuildAndTestStrategy strategy,
        TerminalTools terminal,
        CancellationToken cancellationToken = default);
}
