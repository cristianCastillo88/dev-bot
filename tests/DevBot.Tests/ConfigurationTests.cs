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
    public void CredentialStorageService_SavesAndLoadsApiKey_EncryptedAtRest()
    {
        var service = new CredentialStorageService(_testDir);
        string apiKeyFile = Path.Combine(_testDir, ".devbot", "api_key");

        Assert.Null(service.LoadApiKey());

        string sampleKey = "AIzaSyTestKey123456789";
        service.SaveApiKey(sampleKey);

        Assert.True(File.Exists(apiKeyFile));
        string onDiskContent = File.ReadAllText(apiKeyFile).Trim();

        // Must NOT be stored in plain text
        Assert.NotEqual(sampleKey, onDiskContent);

        // LoadApiKey cleanly decrypts it
        string? loaded = service.LoadApiKey();
        Assert.Equal(sampleKey, loaded);
    }

    [Fact]
    public void CredentialStorageService_MigratesPlaintextKey_Transparently()
    {
        string devbotDir = Path.Combine(_testDir, ".devbot");
        Directory.CreateDirectory(devbotDir);
        string apiKeyFile = Path.Combine(devbotDir, "api_key");

        string plaintextKey = "AIzaSyLegacyPlaintextKey987654";
        File.WriteAllText(apiKeyFile, plaintextKey);

        var service = new CredentialStorageService(_testDir);

        // First read: reads legacy plaintext and auto-encrypts
        string? loaded = service.LoadApiKey();
        Assert.Equal(plaintextKey, loaded);

        // File is now protected
        string migratedDiskContent = File.ReadAllText(apiKeyFile).Trim();
        Assert.NotEqual(plaintextKey, migratedDiskContent);

        // Subsequent read: decrypts protected file cleanly
        string? loadedAgain = service.LoadApiKey();
        Assert.Equal(plaintextKey, loadedAgain);
    }
}
