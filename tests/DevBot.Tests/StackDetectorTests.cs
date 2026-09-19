using DevBot.Cli.Core.Strategies;
using Xunit;

namespace DevBot.Tests;

public class StackDetectorTests : IDisposable
{
    private readonly string _testDir;

    public StackDetectorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "devbot_stack_" + Guid.NewGuid().ToString("N"));
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
    public async Task DetectAsync_IdentifiesDotNetSln()
    {
        File.WriteAllText(Path.Combine(_testDir, "MyApp.sln"), "");
        var detector = new ProjectStackDetector();

        var info = await detector.DetectAsync(_testDir);

        Assert.Equal(ProjectStackType.DotNet, info.StackType);
        Assert.Equal("C#", info.Language);
        Assert.Equal("dotnet", info.BuildTool);
        Assert.Equal("MyApp.sln", info.DetectedFile);
    }

    [Fact]
    public async Task DetectAsync_IdentifiesDotNetCsproj()
    {
        string subDir = Path.Combine(_testDir, "src", "Project");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Project.csproj"), "<Project />");

        var detector = new ProjectStackDetector();
        var info = await detector.DetectAsync(_testDir);

        Assert.Equal(ProjectStackType.DotNet, info.StackType);
        Assert.Equal("C#", info.Language);
        Assert.Contains("Project.csproj", info.DetectedFile);
    }

    [Fact]
    public async Task DetectAsync_IdentifiesNestedDotNetSln()
    {
        string subDir = Path.Combine(_testDir, "Backend-Premier");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Backend-Premier.sln"), "");

        var detector = new ProjectStackDetector();
        var info = await detector.DetectAsync(_testDir);

        Assert.Equal(ProjectStackType.DotNet, info.StackType);
        Assert.Equal("Backend-Premier/Backend-Premier.sln", info.DetectedFile);
    }

    [Fact]
    public async Task DetectAsync_IdentifiesNodeJsTypeScript()
    {
        File.WriteAllText(Path.Combine(_testDir, "package.json"), "{}");
        File.WriteAllText(Path.Combine(_testDir, "tsconfig.json"), "{}");

        var detector = new ProjectStackDetector();
        var info = await detector.DetectAsync(_testDir);

        Assert.Equal(ProjectStackType.NodeJs, info.StackType);
        Assert.Equal("TypeScript", info.Language);
        Assert.Equal("npm", info.BuildTool);
    }

    [Fact]
    public async Task DetectAsync_IdentifiesNodeJsJavaScript()
    {
        File.WriteAllText(Path.Combine(_testDir, "package.json"), "{}");

        var detector = new ProjectStackDetector();
        var info = await detector.DetectAsync(_testDir);

        Assert.Equal(ProjectStackType.NodeJs, info.StackType);
        Assert.Equal("JavaScript", info.Language);
    }

    [Fact]
    public async Task DetectAsync_IdentifiesJavaMaven()
    {
        File.WriteAllText(Path.Combine(_testDir, "pom.xml"), "<project />");

        var detector = new ProjectStackDetector();
        var info = await detector.DetectAsync(_testDir);

        Assert.Equal(ProjectStackType.JavaMaven, info.StackType);
        Assert.Equal("Java", info.Language);
        Assert.Equal("mvn", info.BuildTool);
    }

    [Fact]
    public async Task DetectAsync_IdentifiesPython()
    {
        File.WriteAllText(Path.Combine(_testDir, "pyproject.toml"), "");

        var detector = new ProjectStackDetector();
        var info = await detector.DetectAsync(_testDir);

        Assert.Equal(ProjectStackType.Python, info.StackType);
        Assert.Equal("Python", info.Language);
    }

    [Fact]
    public async Task DetectAsync_IdentifiesGo()
    {
        File.WriteAllText(Path.Combine(_testDir, "go.mod"), "module myapp\n\ngo 1.21");

        var detector = new ProjectStackDetector();
        var info = await detector.DetectAsync(_testDir);

        Assert.Equal(ProjectStackType.Go, info.StackType);
        Assert.Equal("Go", info.Language);
    }

    [Fact]
    public async Task DetectAsync_UnknownDirectory_ReturnsUnknown()
    {
        var detector = new ProjectStackDetector();
        var info = await detector.DetectAsync(_testDir);

        Assert.Equal(ProjectStackType.Unknown, info.StackType);
        Assert.Equal("Unknown", info.Language);
    }

    [Fact]
    public void StrategyResolver_ResolvesAppropriateStrategy()
    {
        var dotNetInfo = new ProjectStackInfo(ProjectStackType.DotNet, "C#", "dotnet", null, Array.Empty<string>(), Array.Empty<string>());
        var nodeInfo = new ProjectStackInfo(ProjectStackType.NodeJs, "TypeScript", "npm", null, Array.Empty<string>(), Array.Empty<string>());
        var mavenInfo = new ProjectStackInfo(ProjectStackType.JavaMaven, "Java", "mvn", null, Array.Empty<string>(), Array.Empty<string>());
        var pyInfo = new ProjectStackInfo(ProjectStackType.Python, "Python", "pytest", null, Array.Empty<string>(), Array.Empty<string>());
        var unknownInfo = new ProjectStackInfo(ProjectStackType.Unknown, "Unknown", "none", null, Array.Empty<string>(), Array.Empty<string>());

        var dotNetStrat = StrategyResolver.Resolve(dotNetInfo);
        var nodeStrat = StrategyResolver.Resolve(nodeInfo);
        var mavenStrat = StrategyResolver.Resolve(mavenInfo);
        var pyStrat = StrategyResolver.Resolve(pyInfo);
        var unknownStrat = StrategyResolver.Resolve(unknownInfo);

        Assert.IsType<DotNetStrategy>(dotNetStrat);
        Assert.IsType<NodeNpmStrategy>(nodeStrat);
        Assert.IsType<MavenStrategy>(mavenStrat);
        Assert.IsType<PythonStrategy>(pyStrat);
        Assert.IsType<GenericFallbackStrategy>(unknownStrat);
    }

    [Fact]
    public void Strategies_ProvideIdiomaticGuidelinesAndVerificationInstructions()
    {
        var dotNet = new DotNetStrategy();
        Assert.Contains("C# 12 / .NET 8", dotNet.GetIdiomaticPromptGuidelines());
        Assert.Contains("dotnet test", dotNet.GetVerificationInstructions());

        var node = new NodeNpmStrategy();
        Assert.Contains("TypeScript", node.GetIdiomaticPromptGuidelines());
        Assert.Contains("npm test", node.GetVerificationInstructions());

        var maven = new MavenStrategy();
        Assert.Contains("Java", maven.GetIdiomaticPromptGuidelines());
        Assert.Contains("mvn test", maven.GetVerificationInstructions());

        var py = new PythonStrategy();
        Assert.Contains("PEP 8", py.GetIdiomaticPromptGuidelines());
        Assert.Contains("pytest", py.GetVerificationInstructions());
    }
}
