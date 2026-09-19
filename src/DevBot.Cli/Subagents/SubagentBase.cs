using System.Text.Json;
using System.Text.RegularExpressions;
using DevBot.Cli.Core;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Spectre.Console;

namespace DevBot.Cli.Subagents;

public abstract class SubagentBase : ISubagent
{
    public abstract string Name { get; }

    protected Kernel CreateSubagentKernel(AgentContext context, IEnumerable<KernelPlugin> plugins)
    {
        var builder = Kernel.CreateBuilder();

        // Use Google's official OpenAI-compatible endpoint to guarantee standard tool calling roles
        builder.AddOpenAIChatCompletion(
            modelId: context.ModelId,
            apiKey: context.ApiKey,
            endpoint: new Uri("https://generativelanguage.googleapis.com/v1beta/openai/")
        );

        foreach (var plugin in plugins)
        {
            builder.Plugins.Add(plugin);
        }

        return builder.Build();
    }

    protected async Task<string> RunChatLoopAsync(
        Kernel kernel,
        ChatHistory history,
        AgentContext context,
        CancellationToken cancellationToken,
        string statusText = "Pensando y analizando con Gemini 3.5 Flash Lite...")
    {
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.2f
        };

        ChatMessageContent response = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan bold"))
            .StartAsync(statusText, async ctx =>
            {
                const int maxRetries = 3;
                const int maxRateLimitRetries = 5;
                int rateLimitAttempt = 0;
                int[] delaysSeconds = { 2, 4, 8 };

                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        return await chatService.GetChatMessageContentAsync(
                            history,
                            executionSettings,
                            kernel,
                            cancellationToken
                        );
                    }
                    catch (Exception ex) when (rateLimitAttempt < maxRateLimitRetries && IsRateLimitError(ex))
                    {
                        rateLimitAttempt++;
                        int waitSeconds = ExtractRetryDelaySeconds(ex, defaultSeconds: 20);
                        ctx.Status($"[yellow]Límite de peticiones alcanzado (429). Esperando {waitSeconds} segundos para reanudar automáticamente...[/]");
                        AnsiConsole.MarkupLine($"[bold yellow]⏳ Límite de peticiones alcanzado (429). Esperando {waitSeconds} segundos para reanudar automáticamente...[/]");
                        await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken);
                        ctx.Status(statusText);
                    }
                    catch (HttpOperationException ex) when (attempt < maxRetries && IsTransientHttpError(ex))
                    {
                        int delay = delaysSeconds[attempt];
                        int code = (int?)ex.StatusCode ?? 503;
                        ctx.Status($"[yellow]⚠ API de Gemini sobrecargada (HTTP {code}). Reintentando en {delay}s (intento {attempt + 1}/{maxRetries})...[/]");
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                        ctx.Status(statusText);
                    }
                    catch (HttpRequestException ex) when (attempt < maxRetries && IsTransientHttpError(ex))
                    {
                        int delay = delaysSeconds[attempt];
                        int code = (int?)ex.StatusCode ?? 503;
                        ctx.Status($"[yellow]⚠ Error temporal de conexión (HTTP {code}). Reintentando en {delay}s (intento {attempt + 1}/{maxRetries})...[/]");
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                        ctx.Status(statusText);
                    }
                    catch (HttpOperationException ex)
                    {
                        AnsiConsole.MarkupLine($"\n[bold red]❌ Error HTTP de Gemini (código {(int?)ex.StatusCode}):[/] {Markup.Escape(ex.Message)}");
                        if (!string.IsNullOrWhiteSpace(ex.ResponseContent))
                        {
                            AnsiConsole.MarkupLine("[bold red]Detalle exacto devuelto por la API de Google:[/]");
                            var errorPanel = new Panel(new Text(ex.ResponseContent))
                            {
                                Header = new PanelHeader("[bold red]Google Gemini Error Payload[/]"),
                                Border = BoxBorder.Heavy
                            };
                            AnsiConsole.Write(errorPanel);
                        }
                        throw;
                    }
                }
            });

        TrackTokenUsage(response, context);

        return response.Content ?? string.Empty;
    }

    private static bool IsRateLimitError(Exception ex)
    {
        string fullMsg = $"{ex.GetType().FullName} {ex.Message} {ex.InnerException?.Message}";
        return fullMsg.Contains("429", StringComparison.OrdinalIgnoreCase) ||
               fullMsg.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase) ||
               fullMsg.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
               fullMsg.Contains("quota", StringComparison.OrdinalIgnoreCase);
    }

    private static int ExtractRetryDelaySeconds(Exception ex, int defaultSeconds = 20)
    {
        string fullMsg = $"{ex.Message} {ex.InnerException?.Message}";
        var match = Regex.Match(fullMsg, @"retry (?:in|after) (\d+(?:\.\d+)?)s?", RegexOptions.IgnoreCase);
        if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out double parsedSec))
        {
            return Math.Max(5, (int)Math.Ceiling(parsedSec) + 1);
        }

        return defaultSeconds;
    }

    private static bool IsTransientHttpError(HttpOperationException ex)
    {
        if (ex.StatusCode.HasValue)
        {
            int code = (int)ex.StatusCode.Value;
            if (code == 503 || code == 429 || code == 500 || code == 502 || code == 504)
                return true;
        }

        string msg = ex.Message;
        return msg.Contains("503", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("429", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientHttpError(HttpRequestException ex)
    {
        if (ex.StatusCode.HasValue)
        {
            int code = (int)ex.StatusCode.Value;
            if (code == 503 || code == 429 || code == 500 || code == 502 || code == 504)
                return true;
        }

        string msg = ex.Message;
        return msg.Contains("503", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("429", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }

    private static void TrackTokenUsage(ChatMessageContent response, AgentContext context)
    {
        if (response.Metadata == null) return;

        try
        {
            if (response.Metadata.TryGetValue("Usage", out var usageObj) && usageObj != null)
            {
                int promptTokens = 0;
                int completionTokens = 0;
                int totalTokens = 0;

                string json = JsonSerializer.Serialize(usageObj);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("PromptTokens", out var pt) && pt.TryGetInt32(out int pVal)) promptTokens = pVal;
                else if (root.TryGetProperty("prompt_tokens", out var pt2) && pt2.TryGetInt32(out int pVal2)) promptTokens = pVal2;

                if (root.TryGetProperty("CompletionTokens", out var ct) && ct.TryGetInt32(out int cVal)) completionTokens = cVal;
                else if (root.TryGetProperty("completion_tokens", out var ct2) && ct2.TryGetInt32(out int cVal2)) completionTokens = cVal2;

                if (root.TryGetProperty("TotalTokenCount", out var tt) && tt.TryGetInt32(out int tVal)) totalTokens = tVal;
                else if (root.TryGetProperty("total_tokens", out var tt2) && tt2.TryGetInt32(out int tVal2)) totalTokens = tVal2;

                if (totalTokens == 0 && (promptTokens > 0 || completionTokens > 0))
                {
                    totalTokens = promptTokens + completionTokens;
                }
                else if (totalTokens > 0 && promptTokens == 0 && completionTokens == 0)
                {
                    promptTokens = totalTokens;
                }

                if (totalTokens > 0)
                {
                    context.Metrics.RecordTokens(promptTokens, completionTokens);
                    context.Logger.LogEvent(context.Metrics.CurrentPhase?.PhaseName ?? "Model", "ModelResponse", $"Tokens consumidos: {totalTokens:N0} (Prompt: {promptTokens:N0}, Completion: {completionTokens:N0})");
                }
            }
        }
        catch
        {
            // Silently continue if metadata structure differs
        }
    }

    public abstract Task<bool> ExecuteAsync(AgentContext context, CancellationToken cancellationToken = default);
}
