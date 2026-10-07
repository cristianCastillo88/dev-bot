using System.Text.RegularExpressions;

namespace DevBot.Cli.Core.Auditing;

public partial class SecretScanner
{
    private record SecretRule(string RuleId, string Description, Regex Pattern);

    [GeneratedRegex(@"(?:AIza[0-9A-Za-z-_]{35}|AQ\.[0-9A-Za-z-_]{40,})")]
    private static partial Regex GoogleApiKeyRegex();

    [GeneratedRegex(@"sk-[A-Za-z0-9_-]{20,}")]
    private static partial Regex OpenAiKeyRegex();

    [GeneratedRegex(@"(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{36}")]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"AKIA[0-9A-Z]{16}")]
    private static partial Regex AwsAccessKeyRegex();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----")]
    private static partial Regex PemPrivateKeyRegex();

    [GeneratedRegex(@"(?:Password|pwd)\s*=\s*['""]?(?<pwd>[^'"";\r\n]{4,})['""]?(?:;|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPasswordRegex();

    [GeneratedRegex(@"AccountKey=[A-Za-z0-9+/=]{86,}", RegexOptions.IgnoreCase)]
    private static partial Regex AzureStorageAccountKeyRegex();

    [GeneratedRegex(@"xox[bpas]-[0-9a-zA-Z-]{10,}")]
    private static partial Regex SlackTokenRegex();

    [GeneratedRegex(@"sk_live_[0-9a-zA-Z]{24,}")]
    private static partial Regex StripeSecretKeyRegex();

    [GeneratedRegex(@"npm_[A-Za-z0-9]{32,36}")]
    private static partial Regex NpmAccessTokenRegex();

    [GeneratedRegex(@"(?:API_KEY|API_SECRET|SECRET_KEY|ACCESS_TOKEN)\s*=\s*['""]?[A-Za-z0-9_\-\+/=]{16,}['""]?", RegexOptions.IgnoreCase)]
    private static partial Regex EnvSecretTokenRegex();

    private static readonly IReadOnlyList<SecretRule> Rules = new List<SecretRule>
    {
        new("SEC001", "Google API / Gemini Key detectada", GoogleApiKeyRegex()),
        new("SEC002", "OpenAI / Anthropic Secret Key detectada", OpenAiKeyRegex()),
        new("SEC003", "GitHub Personal Access Token detectado", GitHubTokenRegex()),
        new("SEC004", "AWS Access Key ID detectada", AwsAccessKeyRegex()),
        new("SEC005", "JSON Web Token (JWT) expuesto", JwtRegex()),
        new("SEC006", "Clave Privada PEM detectada", PemPrivateKeyRegex()),
        new("SEC007", "Contraseña en cadena de conexión", ConnectionStringPasswordRegex()),
        new("SEC008", "Azure Storage Account Key detectada", AzureStorageAccountKeyRegex()),
        new("SEC009", "Slack Bot/User Token detectado", SlackTokenRegex()),
        new("SEC010", "Stripe Secret Key detectada", StripeSecretKeyRegex()),
        new("SEC011", "npm Access Token detectado", NpmAccessTokenRegex()),
        new("SEC012", "Secreto o token sensible en variable de entorno detectado", EnvSecretTokenRegex())
    };

    public IReadOnlyList<SecurityFinding> ScanContent(string filePath, string content)
    {
        var findings = new List<SecurityFinding>();
        if (string.IsNullOrEmpty(content)) return findings;

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            foreach (var rule in Rules)
            {
                var matches = rule.Pattern.Matches(line);
                foreach (Match match in matches)
                {
                    string matchedVal = match.Value;
                    string redacted = RedactSecret(matchedVal);

                    findings.Add(new SecurityFinding(
                        FilePath: filePath,
                        LineNumber: i + 1,
                        RuleId: rule.RuleId,
                        Description: rule.Description,
                        RedactedSnippet: redacted
                    ));
                }
            }
        }

        return findings;
    }

    public static string RedactSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        if (secret.Length <= 8) return "******";

        int keepStart = Math.Min(3, secret.Length / 4);
        int keepEnd = Math.Min(2, secret.Length / 4);

        string start = secret.Substring(0, keepStart);
        string end = secret.Substring(secret.Length - keepEnd);
        return $"{start}****...****{end}";
    }
}
