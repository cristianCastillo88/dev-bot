using DevBot.Cli.Core.Configuration;
using DevBot.Cli.Core.Modes;
using Xunit;

namespace DevBot.Tests;

public class ConfigurationTests : IDisposable
{
    private readonly string _testDir;

    public ConfigurationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "devbot_config_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Fact]
    public void CliArgumentsParser_ParsesFlagsCorrectly()
    {
        string[] args = ["-t", "Agregar tests de integracion", "-d", _testDir, "-m", "gemini-1.5-pro", "-r", "5", "-y", "-M", "bug"];

        var options = CliArgumentsParser.Parse(args);

        Assert.Equal("Agregar tests de integracion", options.TaskDescription);
        Assert.Equal(_testDir, options.RepoDir);
        Assert.Equal("gemini-1.5-pro", options.ModelId);
        Assert.Equal(5, options.MaxRetries);
        Assert.True(options.AutoApprove);
        Assert.Equal(AgentMode.Bug, options.ExplicitMode);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void CliArgumentsParser_RecognizesHelpAndQuickCommands()
    {
        var helpOptions = CliArgumentsParser.Parse(["--help"]);
        Assert.True(helpOptions.ShowHelp);

        var reportOptions = CliArgumentsParser.Parse(["--report"]);
        Assert.True(reportOptions.ShowReport);

        var logsOptions = CliArgumentsParser.Parse(["--logs"]);
        Assert.True(logsOptions.ShowLogs);
    }

    [Fact]
    public void CredentialStorageService_SavesAndLoadsApiKey()
    {
        var service = new CredentialStorageService(_testDir);

        Assert.Null(service.LoadApiKey());

        string sampleKey = "AIzaSyTestKey123456789";
        service.SaveApiKey(sampleKey);

        string? loaded = service.LoadApiKey();
        Assert.Equal(sampleKey, loaded);
    }
}
