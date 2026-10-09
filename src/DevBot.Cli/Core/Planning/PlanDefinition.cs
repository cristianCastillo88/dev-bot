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
            if (m.DependsOn.Count > 0)
            {
                sb.AppendLine($"- **Dependencias:** {string.Join(", ", m.DependsOn)}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Agrupa los hitos en oleadas de ejecución independientes basadas en el grafo de dependencias (DependsOn).
    /// </summary>
    /// <returns>Lista de batches de hitos ordenados topológicamente.</returns>
    public List<List<PlanMilestone>> GetParallelGroups()
    {
        if (Milestones.Count == 0) return [];

        bool hasDependencies = Milestones.Any(m => m.DependsOn != null && m.DependsOn.Count > 0);
        if (!hasDependencies)
        {
            return Milestones.OrderBy(m => m.Order).Select(m => new List<PlanMilestone> { m }).ToList();
        }

        var remaining = new List<PlanMilestone>(Milestones.OrderBy(m => m.Order));
        var completedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var batches = new List<List<PlanMilestone>>();

        while (remaining.Count > 0)
        {
            var currentBatch = remaining.Where(m =>
                m.DependsOn == null ||
                m.DependsOn.Count == 0 ||
                m.DependsOn.All(dep => completedIds.Contains(dep))
            ).ToList();

            if (currentBatch.Count == 0)
            {
                currentBatch.Add(remaining[0]);
            }

            batches.Add(currentBatch);
            foreach (var m in currentBatch)
            {
                remaining.Remove(m);
                if (!string.IsNullOrWhiteSpace(m.Id)) completedIds.Add(m.Id);
                completedIds.Add(m.Order.ToString());
            }
        }

        return batches;
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal deserializing plan JSON: {ex.Message}");
            return null;
        }
    }
}
