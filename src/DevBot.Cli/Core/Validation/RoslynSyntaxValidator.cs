using System.Text.Json;
using DevBot.Cli.Core.Strategies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DevBot.Cli.Core.Validation;

/// <summary>
/// Validador de sintaxis basado en Roslyn para C# y JsonDocument para archivos JSON.
/// Proporciona una única fuente de verdad (DRY) para análisis de sintaxis pre-flight en memoria.
/// </summary>
public class RoslynSyntaxValidator : ISyntaxValidator
{
    /// <summary>
    /// Valida sincrónicamente la sintaxis del contenido sin bloquear threads ni recurrir a procesos externos.
    /// </summary>
    public SyntaxValidationResult Validate(string filePath, string content)
    {
        return ValidateInternal(filePath, content, CancellationToken.None);
    }

    /// <summary>
    /// Valida asincrónicamente la sintaxis del contenido respetando el token de cancelación.
    /// </summary>
    public Task<SyntaxValidationResult> ValidateAsync(string filePath, string content, CancellationToken ct = default)
    {
        return Task.FromResult(ValidateInternal(filePath, content, ct));
    }

    private static SyntaxValidationResult ValidateInternal(string filePath, string content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (string.IsNullOrEmpty(content))
        {
            return SyntaxValidationResult.Success();
        }

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".cs")
        {
            bool hasDeclarations = content.Contains("class ") ||
                                   content.Contains("record ") ||
                                   content.Contains("interface ") ||
                                   content.Contains("struct ") ||
                                   content.Contains("namespace ") ||
                                   content.Contains("enum ");

            if (!hasDeclarations)
            {
                return SyntaxValidationResult.Success();
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(content, cancellationToken: ct);
            var diagnostics = syntaxTree.GetDiagnostics(ct)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(3)
                .ToList();

            if (diagnostics.Count > 0)
            {
                var errors = string.Join("; ", diagnostics.Select(d =>
                {
                    var lineSpan = d.Location.GetLineSpan();
                    int line = lineSpan.StartLinePosition.Line + 1;
                    return $"Línea {line}: {d.GetMessage()}";
                }));

                return SyntaxValidationResult.Failure($"C# syntax error(s): {errors}");
            }
        }
        else if (ext == ".json")
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
            }
            catch (JsonException ex)
            {
                return SyntaxValidationResult.Failure($"JSON syntax error: {ex.Message}");
            }
        }

        return SyntaxValidationResult.Success();
    }
}

/// <summary>
/// Alias por defecto para el validador de sintaxis del sistema.
/// </summary>
public class SyntaxValidator : RoslynSyntaxValidator
{
}
