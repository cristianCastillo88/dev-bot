namespace DevBot.Cli.Core.Memory;

public class RepoComponentInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public List<string> KeyTypes { get; set; } = new();
}

public class RepoMap
{
    public string LastCommitHash { get; set; } = string.Empty;
    public DateTime LastScannedUtc { get; set; } = DateTime.UtcNow;
    public string PrimaryLanguage { get; set; } = string.Empty;
    public List<string> Entrypoints { get; set; } = new();
    public List<string> MainLayers { get; set; } = new();
    public List<RepoComponentInfo> Components { get; set; } = new();
}
