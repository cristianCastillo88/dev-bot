using System.Text.RegularExpressions;

namespace DevBot.Cli.Core.Auditing;

public class SecretScanner
{
    private record SecretRule(string RuleId, string Description, Regex Pattern);

    private static readonly IReadOnlyList<SecretRule> Rules = new List<SecretRule>
    {
        new(
            "SEC001",
            "Google API / Gemini Key detectada",
            new Regex(@"(?:AIza[0-9A-Za-z-_]{35}|AQ\.[0-9A-Za-z-_]{40,})", RegexOptions.Compiled)),

        new(
            "SEC002",
            "OpenAI / Anthropic Secret Key detectada",
            new Regex(@"sk-[A-Za-z0-9_-]{20,}", RegexOptions.Compiled)),

        new(
            "SEC003",
            "GitHub Personal Access Token detectado",
            new Regex(@"(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{36}", RegexOptions.Compiled)),

        new(
            "SEC004",
            "AWS Access Key ID detectada",
            new Regex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled)),

        new(
            "SEC005",
            "JSON Web Token (JWT) expuesto",
            new Regex(@"eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", RegexOptions.Compiled)),

        new(
            "SEC006",
            "Clave Privada PEM detectada",
            new Regex(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----", RegexOptions.Compiled)),

        new(
            "SEC007",
            "Contraseña en cadena de conexión",
            new Regex(@"(?:Password|pwd)\s*=\s*['""]?(?<pwd>[^'"";\r\n]{4,})['""]?(?:;|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase))
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
