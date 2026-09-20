using System.Text.Json;
using DevBot.Cli.Core.Strategies;

namespace DevBot.Cli.Core.Memory;

public class RepositoryMemoryService : IRepositoryMemoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<RepoMap?> LoadRepoMapAsync(string repoRoot, CancellationToken ct = default)
    {
        string mapPath = Path.Combine(repoRoot, ".devbot", "repo_map.json");
        if (!File.Exists(mapPath)) return null;

        try
        {
            string json = await File.ReadAllTextAsync(mapPath, ct);
            return JsonSerializer.Deserialize<RepoMap>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveRepoMapAsync(string repoRoot, RepoMap map, CancellationToken ct = default)
    {
        try
        {
            string devbotDir = Path.Combine(repoRoot, ".devbot");
            if (!Directory.Exists(devbotDir))
            {
                Directory.CreateDirectory(devbotDir);
            }

            string mapPath = Path.Combine(devbotDir, "repo_map.json");
            string json = JsonSerializer.Serialize(map, JsonOptions);
            await File.WriteAllTextAsync(mapPath, json, ct);
        }
        catch
        {
            // Non-fatal if persistence fails
        }
    }

    public async Task<string?> LoadLocalRulesAsync(string repoRoot, CancellationToken ct = default)
    {
        string rulesFile = Path.Combine(repoRoot, ".devbotrules");
        if (File.Exists(rulesFile))
        {
            try
            {
                string text = await File.ReadAllTextAsync(rulesFile, ct);
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            catch { }
        }

        string jsonRulesFile = Path.Combine(repoRoot, "devbot.json");
        if (File.Exists(jsonRulesFile))
        {
            try
            {
                string json = await File.ReadAllTextAsync(jsonRulesFile, ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("rules", out var rulesElement))
                {
                    if (rulesElement.ValueKind == JsonValueKind.String)
                    {
                        return rulesElement.GetString()?.Trim();
                    }
                    if (rulesElement.ValueKind == JsonValueKind.Array)
                    {
                        var items = new List<string>();
                        foreach (var item in rulesElement.EnumerateArray())
                        {
                            items.Add($"- {item.GetString()}");
                        }
                        return string.Join(Environment.NewLine, items);
                    }
                }
                return json.Trim();
            }
            catch { }
        }

        return null;
    }

    public Task<RepoMap> GenerateRepoMapAsync(string repoRoot, ProjectStackInfo stackInfo, string commitHash, CancellationToken ct = default)
    {
        var map = new RepoMap
        {
            LastCommitHash = commitHash,
            LastScannedUtc = DateTime.UtcNow,
            PrimaryLanguage = stackInfo.Language,
            BuildFilePath = stackInfo.DetectedFile
        };

        var entrypoints = new List<string>();
        string[] candidateEntrypointFiles = { "Program.cs", "Startup.cs", "index.ts", "index.js", "main.py", "app.py", "App.java", "main.go" };

        var components = new List<RepoComponentInfo>();
        var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            };

            var dirs = Directory.EnumerateDirectories(repoRoot, "*", enumOptions)
                .Where(d => !IsIgnored(Path.GetRelativePath(repoRoot, d)))
                .ToList();

            foreach (var dir in dirs)
            {
                string dirName = Path.GetFileName(dir);
                string relPath = Path.GetRelativePath(repoRoot, dir).Replace('\\', '/');

                string layer = CategorizeLayer(dirName);
                layers.Add(layer);

                var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .Cast<string>()
                    .ToList();

                foreach (var ep in candidateEntrypointFiles)
                {
                    if (files.Contains(ep, StringComparer.OrdinalIgnoreCase))
                    {
                        entrypoints.Add($"{relPath}/{ep}");
                    }
                }

                if (files.Count > 0 && files.Count <= 20)
                {
                    components.Add(new RepoComponentInfo
                    {
                        Name = dirName,
                        Path = relPath,
                        Layer = layer,
                        KeyTypes = files.Take(8).ToList()
                    });
                }
            }
        }
        catch
        {
            // Non-fatal if filesystem enumeration encounters errors
        }

        map.Entrypoints = entrypoints;
        map.MainLayers = layers.OrderBy(l => l).ToList();
        map.Components = components.Take(15).ToList();

        return Task.FromResult(map);
    }

    private static bool IsIgnored(string relativePath)
    {
        string[] ignored = { ".git", "bin", "obj", "node_modules", ".agent", ".vs", "dist", "build", "coverage", ".cache" };
        var parts = relativePath.Split('/', '\\');
        return parts.Any(p => ignored.Contains(p, StringComparer.OrdinalIgnoreCase));
    }

    private static string CategorizeLayer(string dirName)
    {
        return dirName.ToLowerInvariant() switch
        {
            "controllers" or "routes" or "endpoints" or "api" => "Presentation / API",
            "services" or "usecases" or "handlers" or "logic" => "Business Logic / Application",
            "models" or "entities" or "dto" or "dtos" or "domain" => "Domain / Models",
            "data" or "repositories" or "persistence" or "db" => "Data Access / Infrastructure",
            "core" or "common" or "shared" or "utils" => "Shared / Core",
            "tests" or "test" or "__tests__" => "Testing",
            _ => "Module"
        };
    }
}
