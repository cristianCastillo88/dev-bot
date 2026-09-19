using System.Text;
using System.Text.Json;

namespace DevBot.Cli.Core.Planning;

public class PlanDefinition
{
    public string TaskDescription { get; set; } = string.Empty;
    public string ArchitecturalSummary { get; set; } = string.Empty;
    public List<PlanMilestone> Milestones { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Plan de Implementación Descompuesto");
        sb.AppendLine();
        sb.AppendLine($"**Requerimiento:** {TaskDescription}");
        sb.AppendLine();
        sb.AppendLine("## Resumen de Arquitectura");
        sb.AppendLine(ArchitecturalSummary);
        sb.AppendLine();
        sb.AppendLine($"## Hitos Secuenciales de Trabajo ({Milestones.Count})");
        sb.AppendLine();

        foreach (var m in Milestones.OrderBy(m => m.Order))
        {
            string status = m.IsCompleted ? "[x]" : "[ ]";
            sb.AppendLine($"### Hito {m.Order}: {m.Title} (`{m.Scope}`)");
            sb.AppendLine($"- **Estado:** {status} {(m.IsCompleted ? $"Completado (Commit: `{m.CheckpointCommitHash}`)" : "Pendiente")}");
            sb.AppendLine($"- **Alcance:** {m.Description}");
            sb.AppendLine($"- **Archivos Objetivo:** {(m.TargetFiles.Count > 0 ? string.Join(", ", m.TargetFiles.Select(f => $"`{f}`")) : "Sin archivos específicos")}");
            sb.AppendLine($"- **Comando de Verificación:** `{m.VerificationCommand}`");
            if (m.RequiredSkills.Count > 0)
            {
                sb.AppendLine($"- **Skills Requeridos:** {string.Join(", ", m.RequiredSkills)}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        });
    }

    public static PlanDefinition? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PlanDefinition>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }
}
