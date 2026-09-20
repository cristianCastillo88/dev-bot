using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;

namespace DevBot.Cli.Tools;

public class GitTools
{
    private readonly string _repoRoot;

    public GitTools(string repoRoot)
    {
        _repoRoot = Path.GetFullPath(repoRoot);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunGitAsync(string arguments, int timeoutSeconds = 30)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = _repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, stdout.ToString(), "Git command timed out.");
        }
    }

    public async Task<bool> IsGitRepositoryAsync()
    {
        var result = await RunGitAsync("rev-parse --is-inside-work-tree");
        return result.ExitCode == 0 && result.Output.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> GetCurrentBranchAsync()
    {
        var result = await RunGitAsync("rev-parse --abbrev-ref HEAD");
        return result.ExitCode == 0 ? result.Output : "main";
    }

    public static string CreateSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "devbot-task";
        string slug = text.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');
        if (slug.Length > 40) slug = slug.Substring(0, 40).TrimEnd('-');
        return string.IsNullOrWhiteSpace(slug) ? "devbot-task" : slug;
    }

    [KernelFunction, Description("Creates and switches to a new feature branch in git.")]
    public async Task<string> CheckoutNewBranch(string branchName)
    {
        var result = await RunGitAsync($"checkout -b {branchName}");
        if (result.ExitCode == 0)
        {
            return $"SUCCESS: Checked out new branch '{branchName}'.";
        }

        // If branch already exists, try checking it out
        var switchResult = await RunGitAsync($"checkout {branchName}");
        if (switchResult.ExitCode == 0)
        {
            return $"SUCCESS: Switched to existing branch '{branchName}'.";
        }

        return $"ERROR checking out branch '{branchName}': {result.Error}";
    }

    [KernelFunction, Description("Checks out an existing git branch.")]
    public async Task<string> CheckoutBranch(string branchName)
    {
        var result = await RunGitAsync($"checkout {branchName}");
        return result.ExitCode == 0
            ? $"SUCCESS: Switched to branch '{branchName}'."
            : $"ERROR checking out branch '{branchName}': {result.Error}";
    }

    [KernelFunction, Description("Deletes a git branch.")]
    public async Task<string> DeleteBranch(string branchName)
    {
        var result = await RunGitAsync($"branch -D {branchName}");
        return result.ExitCode == 0
            ? $"SUCCESS: Deleted branch '{branchName}'."
            : $"ERROR deleting branch '{branchName}': {result.Error}";
    }

    [KernelFunction, Description("Stages specified modified files and commits them with the given commit message.")]
    public async Task<string> CommitFiles(IEnumerable<string> files, string message)
    {
        var fileList = files?.ToList() ?? new List<string>();
        if (fileList.Count == 0)
        {
            return "ANOMALY: No modified files registered to commit. Aborting commit to prevent untracked changes.";
        }

        foreach (var file in fileList)
        {
            string safeFile = file.Replace('\\', '/').Replace("\"", "\\\"");
            var addResult = await RunGitAsync($"add \"{safeFile}\"");
            if (addResult.ExitCode != 0)
            {
                return $"ERROR staging file '{file}': {addResult.Error}";
            }
        }

        // Check if there are staged changes
        var statusResult = await RunGitAsync("diff --staged --name-only");
        if (string.IsNullOrWhiteSpace(statusResult.Output))
        {
            return "WARNING: No staged changes found to commit.";
        }

        // Write commit message safely to temp file to avoid CLI escaping corruption on multiline or special chars
        string tempMsgFile = Path.Combine(Path.GetTempPath(), $"devbot_commit_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tempMsgFile, message, Encoding.UTF8);
            string safeMsgPath = tempMsgFile.Replace('\\', '/');
            var commitResult = await RunGitAsync($"commit -F \"{safeMsgPath}\"");
            if (commitResult.ExitCode == 0)
            {
                var hashResult = await RunGitAsync("rev-parse --short HEAD");
                return $"SUCCESS: Committed {fileList.Count} files. Hash: {hashResult.Output}";
            }

            return $"ERROR committing changes: {commitResult.Error}";
        }
        finally
        {
            try { File.Delete(tempMsgFile); } catch { }
        }
    }

    [KernelFunction, Description("Commits staged changes with the given commit message.")]
    public async Task<string> Commit(string message)
    {
        // Check if there are staged changes
        var statusResult = await RunGitAsync("diff --staged --name-only");
        if (string.IsNullOrWhiteSpace(statusResult.Output))
        {
            return "WARNING: No staged changes to commit.";
        }

        string tempMsgFile = Path.Combine(Path.GetTempPath(), $"devbot_commit_{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(tempMsgFile, message, Encoding.UTF8);
            string safeMsgPath = tempMsgFile.Replace('\\', '/');
            var commitResult = await RunGitAsync($"commit -F \"{safeMsgPath}\"");
            if (commitResult.ExitCode == 0)
            {
                var hashResult = await RunGitAsync("rev-parse --short HEAD");
                return $"SUCCESS: Committed changes. Hash: {hashResult.Output}";
            }

            return $"ERROR committing changes: {commitResult.Error}";
        }
        finally
        {
            try { File.Delete(tempMsgFile); } catch { }
        }
    }

    [KernelFunction, Description("Reverts uncommitted changes in the repository.")]
    public async Task<string> Revert()
    {
        await RunGitAsync("reset --hard HEAD");
        await RunGitAsync("clean -fd");
        return "SUCCESS: Reverted working directory to clean state.";
    }

    public async Task<string> ResetHardToCheckpointAsync(string commitHash)
    {
        if (string.IsNullOrWhiteSpace(commitHash) || commitHash.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return await Revert();
        }

        var result = await RunGitAsync($"reset --hard {commitHash}");
        await RunGitAsync("clean -fd");
        return result.ExitCode == 0
            ? $"SUCCESS: Reset repository state to checkpoint '{commitHash}'."
            : $"ERROR resetting to checkpoint '{commitHash}': {result.Error}";
    }

    public async Task<string> GetDiffSummaryAsync()
    {
        var result = await RunGitAsync("diff --stat HEAD~1");
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            var currentDiff = await RunGitAsync("diff --stat");
            return currentDiff.Output;
        }
        return result.Output;
    }

    public async Task<string> GetLastCommitHashAsync()
    {
        var result = await RunGitAsync("rev-parse --short HEAD");
        return result.ExitCode == 0 ? result.Output : "unknown";
    }

    public void EnsureGitExclude(string pattern = ".agent/")
    {
        try
        {
            string gitDir = Path.Combine(_repoRoot, ".git");
            if (!Directory.Exists(gitDir)) return;

            string infoDir = Path.Combine(gitDir, "info");
            if (!Directory.Exists(infoDir))
            {
                Directory.CreateDirectory(infoDir);
            }

            string excludeFile = Path.Combine(infoDir, "exclude");
            if (File.Exists(excludeFile))
            {
                string content = File.ReadAllText(excludeFile);
                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                string cleanPattern = pattern.Trim().TrimEnd('/');
                if (lines.Any(l => {
                    string trimmed = l.Trim().TrimEnd('/');
                    return trimmed.Equals(cleanPattern, StringComparison.OrdinalIgnoreCase);
                }))
                {
                    return;
                }
                File.AppendAllText(excludeFile, Environment.NewLine + pattern + Environment.NewLine);
            }
            else
            {
                File.WriteAllText(excludeFile, pattern + Environment.NewLine);
            }
        }
        catch
        {
            // Non-fatal if .git/info/exclude cannot be modified
        }
    }

    [KernelFunction, Description("Pushes the specified branch to the origin remote.")]
    public async Task<(bool Success, string Output, string Error)> PushBranch(string branchName)
    {
        var result = await RunGitAsync($"push -u origin {branchName}");
        return (result.ExitCode == 0, result.Output, result.Error);
    }

    public async Task<(bool Success, string PrUrl, string Message)> CreatePullRequestAsync(
        string branchName,
        string baseBranch,
        string title,
        string body)
    {
        // 1. Try creating Pull Request via GitHub CLI (`gh`)
        // NOTE: Strictly without --auto or merge flags, waiting exclusively for manual human review!
        string safeTitle = title.Replace("\"", "\\\"");
        string safeBody = body.Replace("\"", "\\\"");
        var ghResult = await RunProcessAsync("gh", $"pr create --title \"{safeTitle}\" --body \"{safeBody}\" --base {baseBranch} --head {branchName}");

        if (ghResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(ghResult.Output))
        {
            string prUrl = ghResult.Output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(l => l.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || l.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                ?? ghResult.Output.Trim();

            return (true, prUrl, "Pull Request creado exitosamente mediante GitHub CLI (gh).");
        }

        // 2. Fallback: Generate standard GitHub Web URL for manual Pull Request creation
        string fallbackUrl = await GetGitHubCompareUrlAsync(branchName, baseBranch);
        if (!string.IsNullOrEmpty(fallbackUrl))
        {
            return (true, fallbackUrl, "GitHub CLI no disponible o no autenticado. Se generó URL web para apertura manual del Pull Request.");
        }

        string errDetails = !string.IsNullOrWhiteSpace(ghResult.Error) ? ghResult.Error : "No se pudo determinar el repositorio remoto de GitHub.";
        return (false, string.Empty, $"No se pudo crear el PR automáticamente ni generar URL: {errDetails}");
    }

    public async Task<string> GetGitHubCompareUrlAsync(string branchName, string baseBranch)
    {
        var remoteResult = await RunGitAsync("remote get-url origin");
        if (remoteResult.ExitCode != 0 || string.IsNullOrWhiteSpace(remoteResult.Output))
        {
            return string.Empty;
        }

        string rawUrl = remoteResult.Output.Trim();
        var match = Regex.Match(rawUrl, @"github\.com[:/](?<owner>[^/\s]+)/(?<repo>[^/\s.]+)(\.git)?", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string owner = match.Groups["owner"].Value;
            string repo = match.Groups["repo"].Value;
            return $"https://github.com/{owner}/{repo}/compare/{baseBranch}...{branchName}?expand=1";
        }

        return string.Empty;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(string fileName, string arguments, int timeoutSeconds = 30)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
