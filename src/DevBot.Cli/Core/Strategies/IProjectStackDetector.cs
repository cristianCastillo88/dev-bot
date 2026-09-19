namespace DevBot.Cli.Core.Strategies;

public interface IProjectStackDetector
{
    Task<ProjectStackInfo> DetectAsync(string repoRoot, CancellationToken ct = default);
}
