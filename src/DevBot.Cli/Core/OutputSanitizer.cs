using System.Text;

namespace DevBot.Cli.Core;

/// <summary>
/// Utilidad unificada para sanitizar y truncar salidas de texto producidas por herramientas y procesos (DRY).
/// Elimina caracteres de control invisibles o corruptos preservando tabulaciones y saltos de línea.
/// </summary>
public static class OutputSanitizer
{
    public const int DefaultMaxLength = 16000;

    /// <summary>
    /// Sanitiza una cadena de texto eliminando caracteres de control no permitidos y opcionalmente truncándola.
    /// </summary>
    /// <param name="output">Cadena cruda a procesar.</param>
    /// <param name="maxLength">Longitud máxima opcional para truncado.</param>
    /// <returns>Cadena limpia y sanitizada.</returns>
    public static string Sanitize(string? output, int? maxLength = null)
    {
        if (string.IsNullOrEmpty(output))
        {
            return string.Empty;
        }

        int capacity = maxLength.HasValue && maxLength.Value > 0
            ? Math.Min(output.Length, maxLength.Value)
            : output.Length;

        var sb = new StringBuilder(capacity);
        foreach (char c in output)
        {
            if (c == '\r' || c == '\n' || c == '\t' || (!char.IsControl(c) && c != '\0'))
            {
                sb.Append(c);
            }
        }

        string clean = sb.ToString();
        if (maxLength.HasValue && maxLength.Value > 0 && clean.Length > maxLength.Value)
        {
            return clean[..maxLength.Value] + "\n... [Contenido truncado para no exceder límites de carga útil] ...";
        }

        return clean;
    }
}
