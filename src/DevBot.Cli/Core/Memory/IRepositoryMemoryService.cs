using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core.Memory;

public interface IRepositoryMemoryService
{
    Task<RepoMap?> LoadRepoMapAsync(string repoRoot, CancellationToken ct = default);
    Task SaveRepoMapAsync(string repoRoot, RepoMap map, CancellationToken ct = default);
    Task<string?> LoadLocalRulesAsync(string repoRoot, CancellationToken ct = default);
    Task<RepoMap> GenerateRepoMapAsync(string repoRoot, ProjectStackInfo stackInfo, string commitHash, CancellationToken ct = default);
}
