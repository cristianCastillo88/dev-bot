using DevBot.Cli.Core;
using DevBot.Cli.Core.Memory;
using DevBot.Cli.Core.Strategies;
using Xunit;

namespace DevBot.Tests;

public class MemoryTests : IDisposable
{
    private readonly string _testDir;
    private readonly RepositoryMemoryService _memoryService = new();

    public MemoryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "devbot_mem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Fact]
    public async Task LoadLocalRulesAsync_ReturnsDevBotRulesContent_WhenFileExists()
    {
        string rulesContent = "Rule 1: Always write tests.\nRule 2: No console.log.";
        await File.WriteAllTextAsync(Path.Combine(_testDir, ".devbotrules"), rulesContent);

        string? loaded = await _memoryService.LoadLocalRulesAsync(_testDir);

        Assert.NotNull(loaded);
        Assert.Equal(rulesContent, loaded);
    }

    [Fact]
    public async Task LoadLocalRulesAsync_ReturnsDevBotJsonRules_WhenStringFormat()
    {
        string json = """
        {
            "rules": "Follow DDD architecture and clean code principles."
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, "devbot.json"), json);

        string? loaded = await _memoryService.LoadLocalRulesAsync(_testDir);

        Assert.NotNull(loaded);
        Assert.Equal("Follow DDD architecture and clean code principles.", loaded);
    }

    [Fact]
    public async Task LoadLocalRulesAsync_ReturnsDevBotJsonRules_WhenArrayFormat()
    {
        string json = """
        {
            "rules": [
                "Use async/await consistently",
                "Keep functions under 40 lines",
                "Add XML comments for public methods"
            ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, "devbot.json"), json);

        string? loaded = await _memoryService.LoadLocalRulesAsync(_testDir);

        Assert.NotNull(loaded);
        Assert.Contains("- Use async/await consistently", loaded);
        Assert.Contains("- Keep functions under 40 lines", loaded);
        Assert.Contains("- Add XML comments for public methods", loaded);
    }

    [Fact]
    public async Task LoadLocalRulesAsync_ReturnsNull_WhenNoRulesFileExists()
    {
        string? loaded = await _memoryService.LoadLocalRulesAsync(_testDir);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAndLoadRepoMapAsync_PersistsAndRetrievesCorrectly()
    {
        var map = new RepoMap
        {
            LastCommitHash = "abc12345",
            LastScannedUtc = DateTime.UtcNow,
            PrimaryLanguage = "C#",
            Entrypoints = new List<string> { "src/Api/Program.cs" },
            MainLayers = new List<string> { "Presentation / API", "Business Logic / Application" },
            Components = new List<RepoComponentInfo>
            {
                new()
                {
                    Name = "Controllers",
                    Path = "src/Api/Controllers",
                    Layer = "Presentation / API",
                    KeyTypes = new List<string> { "UsersController.cs", "OrdersController.cs" }
                }
            }
        };

        await _memoryService.SaveRepoMapAsync(_testDir, map);

        string mapFile = Path.Combine(_testDir, ".devbot", "repo_map.json");
        Assert.True(File.Exists(mapFile));

        var loaded = await _memoryService.LoadRepoMapAsync(_testDir);

        Assert.NotNull(loaded);
        Assert.Equal("abc12345", loaded.LastCommitHash);
        Assert.Equal("C#", loaded.PrimaryLanguage);
        Assert.Single(loaded.Entrypoints);
        Assert.Equal("src/Api/Program.cs", loaded.Entrypoints[0]);
        Assert.Equal(2, loaded.MainLayers.Count);
        Assert.Single(loaded.Components);
        Assert.Equal("Controllers", loaded.Components[0].Name);
    }

    [Fact]
    public async Task LoadRepoMapAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        var loaded = await _memoryService.LoadRepoMapAsync(_testDir);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadRepoMapAsync_ReturnsNull_WhenJsonIsCorrupted()
    {
        string devbotDir = Path.Combine(_testDir, ".devbot");
        Directory.CreateDirectory(devbotDir);
        await File.WriteAllTextAsync(Path.Combine(devbotDir, "repo_map.json"), "{ invalid-json :::");

        var loaded = await _memoryService.LoadRepoMapAsync(_testDir);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task GenerateRepoMapAsync_DiscoversEntrypointsAndLayers()
    {
        // Setup a mock directory layout
        string apiDir = Path.Combine(_testDir, "src", "Controllers");
        string servicesDir = Path.Combine(_testDir, "src", "Services");
        Directory.CreateDirectory(apiDir);
        Directory.CreateDirectory(servicesDir);

        await File.WriteAllTextAsync(Path.Combine(apiDir, "Program.cs"), "// entrypoint");
        await File.WriteAllTextAsync(Path.Combine(apiDir, "HomeController.cs"), "// controller");
        await File.WriteAllTextAsync(Path.Combine(servicesDir, "AuthService.cs"), "// service");

        var stackInfo = new ProjectStackInfo(ProjectStackType.DotNet, "C#", "dotnet", null, Array.Empty<string>(), Array.Empty<string>());
        var map = await _memoryService.GenerateRepoMapAsync(_testDir, stackInfo, "commit-789");

        Assert.Equal("commit-789", map.LastCommitHash);
        Assert.Equal("C#", map.PrimaryLanguage);
        Assert.NotEmpty(map.Entrypoints);
        Assert.Contains(map.Entrypoints, ep => ep.EndsWith("Program.cs"));
        Assert.Contains("Presentation / API", map.MainLayers);
        Assert.Contains("Business Logic / Application", map.MainLayers);
    }

    [Fact]
    public async Task GenerateRepoMapAsync_IgnoresSpecifiedFolders()
    {
        // Setup folders that should be ignored
        string binDir = Path.Combine(_testDir, "bin", "Debug");
        string objDir = Path.Combine(_testDir, "obj");
        string gitDir = Path.Combine(_testDir, ".git");
        string nodeModulesDir = Path.Combine(_testDir, "node_modules");

        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(objDir);
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(nodeModulesDir);

        await File.WriteAllTextAsync(Path.Combine(binDir, "Program.cs"), "// ignore me");
        await File.WriteAllTextAsync(Path.Combine(nodeModulesDir, "index.js"), "// ignore me");

        var stackInfo = new ProjectStackInfo(ProjectStackType.DotNet, "C#", "dotnet", null, Array.Empty<string>(), Array.Empty<string>());
        var map = await _memoryService.GenerateRepoMapAsync(_testDir, stackInfo, "commit-ignore");

        Assert.Empty(map.Entrypoints);
        Assert.Empty(map.Components);
    }

    [Fact]
    public void AgentContext_HoldsLocalRulesAndRepoMap()
    {
        var context = new AgentContext("fake_key", "task for context", _testDir);
        Assert.Null(context.LocalRules);
        Assert.Null(context.RepoMap);
        Assert.NotNull(context.MemoryService);

        context.LocalRules = "Team rule: strict typing";
        context.RepoMap = new RepoMap { LastCommitHash = "hash1" };

        Assert.Equal("Team rule: strict typing", context.LocalRules);
        Assert.Equal("hash1", context.RepoMap.LastCommitHash);
    }
}
