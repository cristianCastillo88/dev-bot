namespace DevBot.Cli.Core.Strategies;

public class ProjectStackDetector : IProjectStackDetector
{
    public Task<ProjectStackInfo> DetectAsync(string repoRoot, CancellationToken ct = default)
    {
        string fullRoot = Path.GetFullPath(repoRoot);
        if (!Directory.Exists(fullRoot))
        {
            return Task.FromResult(new ProjectStackInfo(
                ProjectStackType.Unknown, "Unknown", "none", null, Array.Empty<string>(), Array.Empty<string>()));
        }

        var (sourceDirs, testDirs) = ScanSourceAndTestDirs(fullRoot);

        // 1. Check for .NET (*.sln, *.slnx, *.csproj)
        var topSlnFiles = Directory.EnumerateFiles(fullRoot, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(fullRoot, "*.slnx", SearchOption.TopDirectoryOnly))
            .ToList();

        if (topSlnFiles.Count > 0)
        {
            string rel = Path.GetRelativePath(fullRoot, topSlnFiles[0]).Replace('\\', '/');
            return Task.FromResult(new ProjectStackInfo(
                ProjectStackType.DotNet, "C#", "dotnet", rel, sourceDirs, testDirs));
        }

        // Buscar soluciones en subcarpetas (ej. monorepos o soluciones anidadas como Backend-Premier/)
        var nestedSlnFiles = Directory.EnumerateFiles(fullRoot, "*.sln", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(fullRoot, "*.slnx", SearchOption.AllDirectories))
            .Where(f => !f.Contains(".agent") && !f.Contains("obj") && !f.Contains("bin") && !f.Contains(".git"))
            .Take(1)
            .ToList();

        if (nestedSlnFiles.Count > 0)
        {
            string rel = Path.GetRelativePath(fullRoot, nestedSlnFiles[0]).Replace('\\', '/');
            return Task.FromResult(new ProjectStackInfo(
                ProjectStackType.DotNet, "C#", "dotnet", rel, sourceDirs, testDirs));
        }

        var csprojFiles = Directory.EnumerateFiles(fullRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".agent") && !f.Contains("obj") && !f.Contains("bin") && !f.Contains(".git"))
            .Take(1)
            .ToList();

        if (csprojFiles.Count > 0)
        {
            string rel = Path.GetRelativePath(fullRoot, csprojFiles[0]).Replace('\\', '/');
            return Task.FromResult(new ProjectStackInfo(
                ProjectStackType.DotNet, "C#", "dotnet", rel, sourceDirs, testDirs));
        }

        // 2. Check for Node.js (package.json)
        string packageJson = Path.Combine(fullRoot, "package.json");
        if (File.Exists(packageJson))
        {
            bool isTypeScript = File.Exists(Path.Combine(fullRoot, "tsconfig.json"));
            string language = isTypeScript ? "TypeScript" : "JavaScript";
            return Task.FromResult(new ProjectStackInfo(
                ProjectStackType.NodeJs, language, "npm", "package.json", sourceDirs, testDirs));
        }

        // 3. Check for Java Maven / Gradle (pom.xml, build.gradle)
        string pomXml = Path.Combine(fullRoot, "pom.xml");
        if (File.Exists(pomXml))
        {
            return Task.FromResult(new ProjectStackInfo(
                ProjectStackType.JavaMaven, "Java", "mvn", "pom.xml", sourceDirs, testDirs));
        }

        string buildGradle = Path.Combine(fullRoot, "build.gradle");
        string buildGradleKts = Path.Combine(fullRoot, "build.gradle.kts");
        if (File.Exists(buildGradle) || File.Exists(buildGradleKts))
        {
            string detected = File.Exists(buildGradle) ? "build.gradle" : "build.gradle.kts";
            return Task.FromResult(new ProjectStackInfo(
                ProjectStackType.JavaMaven, "Java", "gradle", detected, sourceDirs, testDirs));
        }

        // 4. Check for Python (pyproject.toml, requirements.txt, setup.py)
        string[] pyMarkers = { "pyproject.toml", "requirements.txt", "setup.py", "Pipfile" };
        foreach (var marker in pyMarkers)
        {
            if (File.Exists(Path.Combine(fullRoot, marker)))
            {
                return Task.FromResult(new ProjectStackInfo(
                    ProjectStackType.Python, "Python", "pytest", marker, sourceDirs, testDirs));
            }
        }

        // 5. Check for Go (go.mod)
        string goMod = Path.Combine(fullRoot, "go.mod");
        if (File.Exists(goMod))
        {
            return Task.FromResult(new ProjectStackInfo(
                ProjectStackType.Go, "Go", "go", "go.mod", sourceDirs, testDirs));
        }

        return Task.FromResult(new ProjectStackInfo(
            ProjectStackType.Unknown, "Unknown", "none", null, sourceDirs, testDirs));
    }

    private static (List<string> SourceDirs, List<string> TestDirs) ScanSourceAndTestDirs(string repoRoot)
    {
        var sourceDirs = new List<string>();
        var testDirs = new List<string>();

        string[] candidateSource = { "src", "lib", "app", "pkg" };
        foreach (var dir in candidateSource)
        {
            string p = Path.Combine(repoRoot, dir);
            if (Directory.Exists(p)) sourceDirs.Add(dir);
        }

        string[] candidateTest = { "test", "tests", "Test", "Tests", "__tests__" };
        foreach (var dir in candidateTest)
        {
            string p = Path.Combine(repoRoot, dir);
            if (Directory.Exists(p)) testDirs.Add(dir);
        }

        return (sourceDirs, testDirs);
    }
}
