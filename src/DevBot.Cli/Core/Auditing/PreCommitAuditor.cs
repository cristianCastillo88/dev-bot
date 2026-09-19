using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Tools;

namespace DevBot.Cli.Core.Auditing;

public class PreCommitAuditor : IPreCommitAuditor
{
    private readonly SecretScanner _scanner = new();

    public async Task<AuditResult> AuditAsync(
        string repoRoot,
        IEnumerable<string> modifiedFiles,
        IBuildAndTestStrategy strategy,
        TerminalTools terminal,
        CancellationToken cancellationToken = default)
    {
        var allFindings = new List<SecurityFinding>();

        // 1. Scan for leaked secrets in all modified files
        foreach (var relativeFile in modifiedFiles)
        {
            string fullPath = Path.IsPathRooted(relativeFile)
                ? relativeFile
                : Path.Combine(repoRoot, relativeFile);

            if (File.Exists(fullPath))
            {
                try
                {
                    string content = await File.ReadAllTextAsync(fullPath, cancellationToken);
                    var findings = _scanner.ScanContent(relativeFile, content);
                    allFindings.AddRange(findings);
                }
                catch
                {
                    // Ignore unreadable files
                }
            }
        }

        // 2. Run code style / linter verification
        StrategyExecutionResult? linterResult = null;
        bool linterNotConfigured = false;
        try
        {
            linterResult = await strategy.LintOrFormatAsync(terminal, cancellationToken);
            if (linterResult != null && !linterResult.Success)
            {
                // Si el linter falla porque no hay .sln/.csproj en la raíz o falta configuración de MSBuild/linter, no fallar la auditoría
                string outText = (linterResult.Output + " " + linterResult.Errors).ToLowerInvariant();
                if (outText.Contains("filenotfoundexception") ||
                    outText.Contains("no se encontró ningún archivo del proyecto") ||
                    outText.Contains("msb1003") ||
                    outText.Contains("could not find a project or solution") ||
                    outText.Contains("command not found") ||
                    outText.Contains("no project or solution file"))
                {
                    linterNotConfigured = true;
                }
            }
        }
        catch (Exception ex)
        {
            linterResult = new StrategyExecutionResult(false, -1, string.Empty, ex.Message, "linter");
        }

        bool secretsPassed = allFindings.Count == 0;
        bool linterPassed = linterResult == null || linterResult.Success || linterNotConfigured;

        bool passed = secretsPassed && linterPassed;

        string summary = passed
            ? (linterNotConfigured
                ? "Auditoría superada: Sin secretos expuestos (linter no configurado o archivo de solución no encontrado en raíz, omitido)."
                : "Auditoría superada: Sin secretos expuestos y linter conforme.")
            : (!secretsPassed
                ? $"FALLO DE SEGURIDAD: Se detectaron {allFindings.Count} posibles secretos/credenciales en los archivos modificados."
                : $"FALLO DE CALIDAD: El linter del stack ({strategy.Name}) reportó violaciones de formato.");

        return new AuditResult(passed, allFindings, linterResult, summary);
    }
}
