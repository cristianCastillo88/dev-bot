using System.Text;

namespace DevBot.Cli.Core;

public static class RunReportGenerator
{
    public static string GenerateReport(AgentContext context, string? diffSummary = null)
    {
        var sb = new StringBuilder();
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string fileTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        sb.AppendLine("# 🤖 DevBot - Informe Ejecutivo de Ejecución");
        sb.AppendLine();
        sb.AppendLine($"> **Fecha y Hora:** {timestamp}  ");
        sb.AppendLine($"> **Modelo IA:** `{context.ModelId}`  ");
        sb.AppendLine($"> **Estado:** {(context.Metrics.Success ? "**✔ COMPLETADO CON ÉXITO (ALL_PASS)**" : "**❌ REVERTIDO (ROLLBACK)**")}  ");
        sb.AppendLine();

        // 1. Git & Deployment Overview
        sb.AppendLine("## 1. Resumen de Git y Pull Request");
        sb.AppendLine();
        sb.AppendLine($"- **Rama Base:** `{context.OriginalBranch}`");
        sb.AppendLine($"- **Rama de Trabajo:** `{context.TargetBranch}`");
        sb.AppendLine($"- **Rama Remota Publicada:** `origin/{context.TargetBranch}`");
        if (!string.IsNullOrEmpty(context.Metrics.CommitHash))
        {
            sb.AppendLine($"- **Commit Hash:** `{context.Metrics.CommitHash}`");
        }
        if (!string.IsNullOrEmpty(context.PullRequestUrl))
        {
            sb.AppendLine($"- **🔗 Pull Request para Revisión Humana:** [{context.PullRequestUrl}]({context.PullRequestUrl})");
            sb.AppendLine();
            sb.AppendLine("> [!IMPORTANT]");
            sb.AppendLine("> Por política estricta de seguridad, el Pull Request queda abierto esperando auditoría manual por parte del desarrollador. No se ha aplicado automerge.");
        }
        sb.AppendLine();

        // 2. Assigned Task
        sb.AppendLine("## 2. Requerimiento Asignado");
        sb.AppendLine();
        sb.AppendLine("```text");
        sb.AppendLine(string.IsNullOrWhiteSpace(context.TaskDescription) ? context.ReadTask() : context.TaskDescription);
        sb.AppendLine("```");
        sb.AppendLine();

        // 3. Plan Milestones & Git Checkpoints
        if (context.Plan != null && context.Plan.Milestones.Count > 0)
        {
            sb.AppendLine("## 3. Descomposición Jerárquica de Hitos y Checkpoints");
            sb.AppendLine();
            sb.AppendLine($"El requerimiento fue descompuesto por el **Planner Agent** en **{context.Plan.Milestones.Count}** hitos atómicos:");
            sb.AppendLine();
            sb.AppendLine("| # | Hito | Capa (Scope) | Estado | Commit Checkpoint | Comando de Verificación |");
            sb.AppendLine("| :-: | :--- | :--- | :---: | :---: | :--- |");
            foreach (var m in context.Plan.Milestones.OrderBy(m => m.Order))
            {
                string status = m.IsCompleted ? "✔ Completado" : "❌ Pendiente / Fallido";
                string hash = !string.IsNullOrEmpty(m.CheckpointCommitHash) ? $"`{m.CheckpointCommitHash}`" : "-";
                sb.AppendLine($"| {m.Order} | **{m.Title}** | `{m.Scope}` | {status} | {hash} | `{m.VerificationCommand}` |");
            }
            sb.AppendLine();
        }

        // 4. Phase Metrics Table
        sb.AppendLine("## 4. Desglose de Métricas y Telemetría por Agente");
        sb.AppendLine();
        sb.AppendLine("| Fase / Subagente | Duración | Prompt Tokens | Completion Tokens | Total Tokens | Herramientas Invocadas | Resultado |");
        sb.AppendLine("| :--- | :---: | :---: | :---: | :---: | :--- | :---: |");

        foreach (var phase in context.Metrics.Phases)
        {
            string toolsStr = phase.ToolCalls.Count > 0
                ? string.Join(", ", phase.ToolCalls.Select(tc => $"`{tc.Key}` ({tc.Value})"))
                : "*Ninguna*";
            string statusStr = phase.Success ? "✔ Éxito" : "❌ Falla / Alerta";

            sb.AppendLine($"| **{phase.PhaseName}** | {phase.Duration.TotalSeconds:F1}s | {phase.PromptTokens:N0} | {phase.CompletionTokens:N0} | **{phase.TotalTokens:N0}** | {toolsStr} | {statusStr} |");
        }

        sb.AppendLine();
        sb.AppendLine($"- **Tiempo Total de Ejecución:** {context.Metrics.Elapsed.TotalSeconds:F1} segundos");
        sb.AppendLine($"- **Tokens Totales Consumidos:** {context.Metrics.TokensConsumed:N0}");
        sb.AppendLine($"- **Ciclos Coder/Reviewer Requeridos:** {context.Metrics.CoderRetries}");
        sb.AppendLine();

        // 4. Detailed Agent Findings
        sb.AppendLine("## 4. Bitácora de Subagentes");
        sb.AppendLine();

        // Scout
        string scoutReport = context.ReadScoutReport();
        sb.AppendLine("### 🔍 Scout Agent (Análisis Arquitectónico)");
        if (!string.IsNullOrWhiteSpace(scoutReport))
        {
            sb.AppendLine(scoutReport.Trim());
        }
        else
        {
            sb.AppendLine("*No se generó informe Scout o la fase fue omitida.*");
        }
        sb.AppendLine();

        // Planner
        if (context.Plan != null)
        {
            sb.AppendLine("### 📋 Planner Agent (Estrategia y Descomposición de Hitos)");
            sb.AppendLine($"> **Hitos Definidos:** {context.Plan.Milestones.Count} hitos atómicos  ");
            sb.AppendLine();
            sb.AppendLine(context.Plan.ArchitecturalSummary.Trim());
            sb.AppendLine();
        }

        // Reviewer
        string reviewResults = context.ReadReviewResults();
        sb.AppendLine("### 🧪 Reviewer Agent (Pruebas y Verificación Determinista)");
        if (!string.IsNullOrWhiteSpace(reviewResults))
        {
            sb.AppendLine(reviewResults.Trim());
        }
        else
        {
            sb.AppendLine("*No se registraron resultados de verificación.*");
        }
        sb.AppendLine();

        // 5. Modified Files
        sb.AppendLine("## 5. Archivos Quirúrgicamente Modificados");
        sb.AppendLine();
        if (context.ModifiedFiles.Count > 0)
        {
            foreach (var file in context.ModifiedFiles)
            {
                sb.AppendLine($"- `{file}`");
            }
        }
        else
        {
            sb.AppendLine("*No se registraron archivos modificados.*");
        }
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(diffSummary))
        {
            sb.AppendLine("### Resumen de Diferencias (`git diff --stat`)");
            sb.AppendLine("```text");
            sb.AppendLine(diffSummary.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // 6. Tool Calling Aggregated Audit
        sb.AppendLine("## 6. Auditoría Cuantitativa de Herramientas");
        sb.AppendLine();
        var allTools = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var phase in context.Metrics.Phases)
        {
            foreach (var kv in phase.ToolCalls)
            {
                allTools[kv.Key] = allTools.GetValueOrDefault(kv.Key, 0) + kv.Value;
            }
        }

        if (allTools.Count > 0)
        {
            sb.AppendLine("| Herramienta | Total Invocaciones | Propósito Principal |");
            sb.AppendLine("| :--- | :---: | :--- |");
            foreach (var kv in allTools.OrderByDescending(x => x.Value))
            {
                string purpose = kv.Key switch
                {
                    "ReadFile" => "Lectura de archivos y rangos específicos",
                    "ListFiles" => "Exploración de directorios",
                    "SearchCode" => "Búsqueda de patrones y símbolos",
                    "WriteFile" => "Creación / sobrescritura de archivos",
                    "ApplyDiff" => "Modificación quirúrgica sin tocar el resto del archivo",
                    "RunCommand" => "Ejecución de pruebas o linters en consola",
                    "CheckoutNewBranch" => "Creación de rama de trabajo en Git",
                    "CommitFiles" => "Stage y commit quirúrgico de archivos",
                    _ => "Operación de soporte"
                };
                sb.AppendLine($"| `{kv.Key}` | {kv.Value} | {purpose} |");
            }
        }
        else
        {
            sb.AppendLine("*No se registraron llamadas a herramientas.*");
        }
        sb.AppendLine();

        // 7. Footer
        sb.AppendLine("---");
        sb.AppendLine($"*Reporte generado automáticamente por **DevBot v1.1.0** en `{timestamp}`.*");

        string reportContent = sb.ToString();

        // Save report to .agent/latest_run_report.md and .agent/reports/report_<timestamp>.md
        try
        {
            if (!Directory.Exists(context.ReportsDir))
            {
                Directory.CreateDirectory(context.ReportsDir);
            }

            File.WriteAllText(context.LatestReportPath, reportContent, Encoding.UTF8);
            string historicalPath = Path.Combine(context.ReportsDir, $"report_{fileTimestamp}.md");
            File.WriteAllText(historicalPath, reportContent, Encoding.UTF8);
        }
        catch
        {
            // Non-fatal if writing to file fails
        }

        return reportContent;
    }
}
