namespace DevBot.Cli.Core.Strategies;

public static class StrategyResolver
{
    private static readonly Dictionary<ProjectStackType, Func<ProjectStackInfo, IBuildAndTestStrategy>> Registry = new()
    {
        [ProjectStackType.DotNet] = (info) => new DotNetStrategy(info.DetectedFile),
        [ProjectStackType.NodeJs] = (_) => new NodeNpmStrategy(),
        [ProjectStackType.JavaMaven] = (_) => new MavenStrategy(),
        [ProjectStackType.Python] = (_) => new PythonStrategy(),
        [ProjectStackType.Unknown] = (_) => new GenericFallbackStrategy()
    };

    public static IBuildAndTestStrategy Resolve(ProjectStackInfo stackInfo)
    {
        if (Registry.TryGetValue(stackInfo.StackType, out var factory))
        {
            return factory(stackInfo);
        }
        return new GenericFallbackStrategy();
    }
}
