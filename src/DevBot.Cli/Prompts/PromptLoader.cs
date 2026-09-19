namespace DevBot.Cli.Prompts;

public static class PromptLoader
{
    public static string LoadPrompt(string promptFileName, string fallbackContent)
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string path1 = Path.Combine(baseDir, "Prompts", promptFileName);
            if (File.Exists(path1)) return File.ReadAllText(path1);

            string path2 = Path.Combine(baseDir, promptFileName);
            if (File.Exists(path2)) return File.ReadAllText(path2);

            string currentDir = Directory.GetCurrentDirectory();
            string path3 = Path.Combine(currentDir, "src", "DevBot.Cli", "Prompts", promptFileName);
            if (File.Exists(path3)) return File.ReadAllText(path3);
        }
        catch
        {
            // Fall through to fallback
        }

        return fallbackContent;
    }
}
