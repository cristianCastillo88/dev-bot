using DevBot.Cli.Core;
using DevBot.Cli.Core.Planning;
using DevBot.Cli.Core.Strategies;
using DevBot.Cli.Core.Validation;
using DevBot.Cli.Tools;
using Xunit;

namespace DevBot.Tests;

public class ToolsTests : IDisposable
{
    private readonly string _testDir;

    public ToolsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "devbot_tests_" + Guid.NewGuid().ToString("N"));
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
    public void FileTools_WriteAndReadFileWithRanges_Succeeds()
    {
        var tools = new FileTools(_testDir);
        string sampleCode = "line1\nline2\nline3\nline4\nline5";

        string writeResult = tools.WriteFile("test.txt", sampleCode);
        Assert.Contains("SUCCESS", writeResult);

        string readFull = tools.ReadFile("test.txt");
        Assert.Contains("line1", readFull);
        Assert.Contains("line5", readFull);

        // Read range 2 to 4
        string readRange = tools.ReadFile("test.txt", startLine: 2, endLine: 4);
        Assert.Contains("line2", readRange);
        Assert.Contains("line3", readRange);
        Assert.Contains("line4", readRange);
        Assert.DoesNotContain("line1", readRange);
        Assert.DoesNotContain("line5", readRange);
    }

    [Fact]
    public void FileTools_ApplyDiff_SurgicalReplacementWorks()
    {
        var tools = new FileTools(_testDir);
        string original = """
            public class Calculator
            {
                public int Add(int a, int b) => a - b; // bug here
            }
            """;

        tools.WriteFile("Calculator.cs", original);

        string diffResult = tools.ApplyDiff(
            "Calculator.cs",
            originalSnippet: "public int Add(int a, int b) => a - b; // bug here",
            replacementSnippet: "public int Add(int a, int b) => a + b;"
        );

        Assert.Contains("SUCCESS", diffResult);

        string updated = File.ReadAllText(Path.Combine(_testDir, "Calculator.cs"));
        Assert.Contains("public int Add(int a, int b) => a + b;", updated);
        Assert.DoesNotContain("a - b", updated);
    }

    [Fact]
    public void FileTools_ApplyDiff_FailsOnNonUniqueSnippet()
    {
        var tools = new FileTools(_testDir);
        string contentWithDuplicates = "duplicate\nsomething\nduplicate";
        tools.WriteFile("dup.txt", contentWithDuplicates);

        string diffResult = tools.ApplyDiff(
            "dup.txt",
            originalSnippet: "duplicate",
            replacementSnippet: "replaced"
        );

        Assert.Contains("ERROR", diffResult);
        Assert.Contains("matches multiple locations", diffResult);
    }

    [Fact]
    public void FileTools_SearchCode_FindsOccurrences()
    {
        var tools = new FileTools(_testDir);
        tools.WriteFile("Service.cs", "public void ProcessOrder() { var x = 42; }");
        tools.WriteFile("Other.txt", "Random text");

        string result = tools.SearchCode("ProcessOrder");
        Assert.Contains("Service.cs:1", result);
        Assert.Contains("ProcessOrder", result);
    }

    [Fact]
    public void FileTools_PluginLeastPrivilege_ScoutHasNoWriteFunctions()
    {
        var scoutPlugin = FileTools.CreateScoutPlugin(_testDir);
        var functionNames = scoutPlugin.Select(f => f.Name).ToList();

        Assert.Contains("ReadFile", functionNames);
        Assert.Contains("ListFiles", functionNames);
        Assert.Contains("SearchCode", functionNames);
        Assert.DoesNotContain("WriteFile", functionNames);
        Assert.DoesNotContain("ApplyDiff", functionNames);
    }

    [Fact]
    public void FileTools_PluginLeastPrivilege_CoderHasNoSearchOrListFunctions()
    {
        var coderPlugin = FileTools.CreateCoderPlugin(_testDir);
        var functionNames = coderPlugin.Select(f => f.Name).ToList();

        Assert.Contains("ReadFile", functionNames);
        Assert.Contains("WriteFile", functionNames);
        Assert.Contains("ApplyDiff", functionNames);
        Assert.DoesNotContain("SearchCode", functionNames);
        Assert.DoesNotContain("ListFiles", functionNames);
    }

    [Fact]
    public async Task TerminalTools_RunCommand_ExecutesAndCapturesOutput()
    {
        var tools = new TerminalTools(_testDir);
        string result = await tools.RunCommand("dotnet", "--version");

        Assert.Contains("Exit Code: 0", result);
        Assert.Contains("--- STDOUT ---", result);
    }

    [Fact]
    public void GitTools_CreateSlug_FormatsProperly()
    {
        string slug1 = GitTools.CreateSlug("Fix null reference in UserService.cs!");
        Assert.Equal("fix-null-reference-in-userservicecs", slug1);

        string slug2 = GitTools.CreateSlug("   Special @#$ Characters & Multiple   Spaces  ");
        Assert.Equal("special-characters-multiple-spaces", slug2);
    }

    [Fact]
    public void FileTools_TracksModifiedFiles_WhenWriteOrDiffSucceeds()
    {
        var context = new AgentContext(_testDir, "test-api-key");
        var tools = new FileTools(_testDir, context);

        tools.WriteFile("test_file.txt", "Initial content");
        Assert.Contains("test_file.txt", context.ModifiedFiles);

        tools.ApplyDiff("test_file.txt", "Initial content", "Updated content");
        Assert.Contains("test_file.txt", context.ModifiedFiles);
        Assert.Single(context.ModifiedFiles); // Should not duplicate
    }

    [Fact]
    public void GitTools_EnsureGitExclude_AddsAgentPattern()
    {
        // Simulate a .git folder
        string gitInfoDir = Path.Combine(_testDir, ".git", "info");
        Directory.CreateDirectory(gitInfoDir);

        var gitTools = new GitTools(_testDir);
        gitTools.EnsureGitExclude(".agent/");

        string excludeFile = Path.Combine(gitInfoDir, "exclude");
        Assert.True(File.Exists(excludeFile));
        string content = File.ReadAllText(excludeFile);
        Assert.Contains(".agent/", content);

        // Calling it again should not duplicate
        gitTools.EnsureGitExclude(".agent/");
        int occurrences = content.Split(".agent/").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public async Task GitTools_CommitFiles_EmptyListReportsAnomaly()
    {
        var gitTools = new GitTools(_testDir);
        string result = await gitTools.CommitFiles(new List<string>(), "feat: test");
        Assert.Contains("ANOMALY", result);
    }

    [Fact]
    public void FileTools_ApplyDiff_WhitespaceTolerance_MatchesAndReplaces()
    {
        var tools = new FileTools(_testDir);
        string original = """
            public class Service
            {
                public void DoWork()
                {
                    var x = 10;
                    var y = 20;
                }
            }
            """;

        tools.WriteFile("Service.cs", original);

        // Intentionally provide slightly different indentation in originalSnippet
        string diffResult = tools.ApplyDiff(
            "Service.cs",
            originalSnippet: """
              var x = 10;
              var y = 20;
            """,
            replacementSnippet: """
                    var x = 100;
                    var y = 200;
            """
        );

        Assert.Contains("SUCCESS", diffResult);
        string updated = File.ReadAllText(Path.Combine(_testDir, "Service.cs"));
        Assert.Contains("var x = 100;", updated);
        Assert.Contains("var y = 200;", updated);
    }

    [Fact]
    public void FileTools_ListFiles_MatchesFilesWithoutExtensionByDefault()
    {
        var tools = new FileTools(_testDir);
        tools.WriteFile("Dockerfile", "FROM mcr.microsoft.com/dotnet/sdk:8.0");
        tools.WriteFile("README.md", "# DevBot");

        string listing = tools.ListFiles();
        Assert.Contains("Dockerfile", listing);
        Assert.Contains("README.md", listing);
    }

    [Fact]
    public void FileTools_SearchCode_SkipsMinifiedFiles()
    {
        var tools = new FileTools(_testDir);
        tools.WriteFile("bundle.min.js", "function secret_min_token() {}");
        tools.WriteFile("app.js", "function secret_min_token() {}");

        string result = tools.SearchCode("secret_min_token");
        Assert.Contains("app.js", result);
        Assert.DoesNotContain("bundle.min.js", result);
    }

    [Fact]
    public async Task TerminalTools_RecordsExitCodeInContext()
    {
        var context = new AgentContext(_testDir, "test-api-key");
        var tools = new TerminalTools(_testDir, context);

        string result = await tools.RunCommand("dotnet", "--version");

        Assert.Contains("Exit Code: 0", result);
        Assert.Equal(0, context.LastCommandExitCode);
        Assert.Single(context.CommandExecutionHistory);
        Assert.True(context.CommandExecutionHistory[0].Success);
    }

    [Fact]
    public void FileTools_ApplyDiff_RejectsSyntaxErrors()
    {
        var tools = new FileTools(_testDir);
        string validCode = "public class Calculator { public int Add(int a, int b) => a + b; }";
        tools.WriteFile("Calculator.cs", validCode);

        // Intentionally introduce broken C# syntax: unclosed expression
        string diffResult = tools.ApplyDiff(
            "Calculator.cs",
            originalSnippet: "public int Add(int a, int b) => a + b;",
            replacementSnippet: "public int Add(int a, int b) => a + ; // broken syntax"
        );

        Assert.Contains("PRE-FLIGHT SYNTAX ERROR", diffResult);
        string currentContent = File.ReadAllText(Path.Combine(_testDir, "Calculator.cs"));
        Assert.Equal(validCode, currentContent); // File must be untouched
    }

    [Fact]
    public void FileTools_ApplyMultiDiff_AppliesMultipleEditsAtomically()
    {
        var tools = new FileTools(_testDir);
        string original = """
            using System;

            public class Example
            {
                public int A = 1;
                public int B = 2;
            }
            """;
        tools.WriteFile("Example.cs", original);

        string diffsJson = """
            [
                {
                    "originalSnippet": "using System;",
                    "replacementSnippet": "using System;\nusing System.Collections.Generic;"
                },
                {
                    "originalSnippet": "public int B = 2;",
                    "replacementSnippet": "public int B = 200;"
                }
            ]
            """;

        string result = tools.ApplyMultiDiff("Example.cs", diffsJson);
        Assert.Contains("SUCCESS", result);

        string updated = File.ReadAllText(Path.Combine(_testDir, "Example.cs"));
        Assert.Contains("using System.Collections.Generic;", updated);
        Assert.Contains("public int B = 200;", updated);
    }

    [Fact]
    public void FileTools_ApplyMultiDiff_AbortsTransactionIfAnyChunkFails()
    {
        var tools = new FileTools(_testDir);
        string original = "public class Test { public int X = 10; }";
        tools.WriteFile("Test.cs", original);

        string diffsJson = """
            [
                {
                    "originalSnippet": "public int X = 10;",
                    "replacementSnippet": "public int X = 20;"
                },
                {
                    "originalSnippet": "this snippet does not exist in the file",
                    "replacementSnippet": "replacement"
                }
            ]
            """;

        string result = tools.ApplyMultiDiff("Test.cs", diffsJson);
        Assert.Contains("TRANSACTION ABORTED", result);

        string fileContent = File.ReadAllText(Path.Combine(_testDir, "Test.cs"));
        Assert.Equal(original, fileContent); // Ensure first chunk was rolled back
    }

    [Fact]
    public void FileTools_ScopeGuardrails_BlocksModificationOutsideCurrentMilestone()
    {
        var context = new AgentContext(_testDir, "test-api-key");
        context.CurrentMilestone = new PlanMilestone
        {
            Order = 1,
            Title = "Implement Feature A",
            Scope = "Core",
            TargetFiles = ["src/AllowedFile.cs"]
        };

        var tools = new FileTools(_testDir, context);
        tools.WriteFile("src/AllowedFile.cs", "public class AllowedFile {}");
        tools.WriteFile("src/ForbiddenFile.cs", "public class ForbiddenFile {}");

        // Editing forbidden file should be blocked
        string blockedResult = tools.ApplyDiff(
            "src/ForbiddenFile.cs",
            "public class ForbiddenFile {}",
            "public class ForbiddenFile { public int X = 1; }"
        );

        Assert.Contains("SCOPE ERROR", blockedResult);
        Assert.Contains("Milestone #1", blockedResult);

        // Editing allowed file should succeed
        string allowedResult = tools.ApplyDiff(
            "src/AllowedFile.cs",
            "public class AllowedFile {}",
            "public class AllowedFile { public int X = 1; }"
        );

        Assert.Contains("SUCCESS", allowedResult);
        Assert.Contains("src/AllowedFile.cs", context.MilestoneModifiedFiles);
    }

    [Fact]
    public void FileTools_CoderPlugin_ContainsApplyMultiDiff()
    {
        var coderPlugin = FileTools.CreateCoderPlugin(_testDir);
        var functionNames = coderPlugin.Select(f => f.Name).ToList();

        Assert.Contains("ApplyMultiDiff", functionNames);
    }

    [Fact]
    public void FileTools_PolyglotValidation_DelegatesToActivePythonStrategy()
    {
        var context = new AgentContext(_testDir, "test-api-key")
        {
            Strategy = new PythonStrategy()
        };

        var tools = new FileTools(_testDir, context);
        string writeResult = tools.WriteFile("script.py", "def add(a, b):\n    return a + b\n");
        Assert.Contains("SUCCESS", writeResult);
    }

    [Fact]
    public void RoslynSyntaxValidator_ValidatesCSharpAndJson_Correctly()
    {
        ISyntaxValidator validator = new RoslynSyntaxValidator();

        // Valid C#
        var validCs = validator.Validate("Test.cs", "namespace Demo; public class Foo { public int Bar { get; set; } }");
        Assert.True(validCs.IsValid);
        Assert.Null(validCs.ErrorMessage);

        // Invalid C#
        var invalidCs = validator.Validate("Test.cs", "namespace Demo; public class Foo { public int Bar { get; set } }"); // missing semicolon
        Assert.False(invalidCs.IsValid);
        Assert.NotNull(invalidCs.ErrorMessage);
        Assert.Contains("C# syntax error", invalidCs.ErrorMessage);

        // Valid JSON
        var validJson = validator.Validate("appsettings.json", "{ \"Logging\": { \"LogLevel\": { \"Default\": \"Information\" } } }");
        Assert.True(validJson.IsValid);

        // Invalid JSON
        var invalidJson = validator.Validate("appsettings.json", "{ \"Logging\": { incomplete ");
        Assert.False(invalidJson.IsValid);
        Assert.NotNull(invalidJson.ErrorMessage);
        Assert.Contains("JSON syntax error", invalidJson.ErrorMessage);

        // Non-code files are always considered valid
        var otherFile = validator.Validate("readme.txt", "Some random text without structure");
        Assert.True(otherFile.IsValid);
    }

    [Fact]
    public void OutputSanitizer_FiltersControlCharsAndTruncates_Correctly()
    {
        // 1. Keeps \r, \n, \t and normal chars
        string raw = "Hello\tWorld!\r\nLine 2";
        Assert.Equal("Hello\tWorld!\r\nLine 2", OutputSanitizer.Sanitize(raw));

        // 2. Removes null bytes and control chars (e.g. \0, \x01, \x08, etc.)
        string corrupted = "Hello\0\u0001\u0008World";
        Assert.Equal("HelloWorld", OutputSanitizer.Sanitize(corrupted));

        // 3. Null or empty returns empty
        Assert.Equal(string.Empty, OutputSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, OutputSanitizer.Sanitize(""));

        // 4. Truncates when length exceeds maxLength
        string longText = new string('A', 50);
        string sanitized = OutputSanitizer.Sanitize(longText, 20);
        Assert.StartsWith(new string('A', 20), sanitized);
        Assert.Contains("truncado", sanitized);
    }

    [Fact]
    public void FileReadOperations_And_FileWriteOperations_WorkIndependently()
    {
        var readOps = new FileReadOperations(_testDir);
        var writeOps = new FileWriteOperations(_testDir);

        // Write with writeOps
        string writeRes = writeOps.WriteFile("independent.txt", "line1\nline2\nline3");
        Assert.Contains("SUCCESS", writeRes);

        // Read with readOps
        string readRes = readOps.ReadFile("independent.txt");
        Assert.Contains("line1", readRes);
        Assert.Contains("line3", readRes);

        // List with readOps
        string listRes = readOps.ListFiles(".");
        Assert.Contains("independent.txt", listRes);

        // Search with readOps
        string searchRes = readOps.SearchCode("line2");
        Assert.Contains("independent.txt", searchRes);

        // Apply diff with writeOps
        string diffRes = writeOps.ApplyDiff("independent.txt", "line2", "line2_modified");
        Assert.Contains("SUCCESS", diffRes);
        string updatedRead = readOps.ReadFile("independent.txt");
        Assert.Contains("line2_modified", updatedRead);
    }

    [Fact]
    public void ToolPluginFactory_ProducesExpectedPlugins()
    {
        var scoutPlugin = ToolPluginFactory.CreateScoutPlugin(_testDir);
        var coderPlugin = ToolPluginFactory.CreateCoderPlugin(_testDir);
        var reviewerPlugin = ToolPluginFactory.CreateReviewerPlugin(_testDir);

        Assert.Equal("FileTools", scoutPlugin.Name);
        Assert.Equal(3, scoutPlugin.Count()); // ReadFile, ListFiles, SearchCode

        Assert.Equal("FileTools", coderPlugin.Name);
        Assert.Equal(4, coderPlugin.Count()); // ReadFile, WriteFile, ApplyDiff, ApplyMultiDiff

        Assert.Equal("FileTools", reviewerPlugin.Name);
        Assert.Single(reviewerPlugin); // ReadFile
    }

    [Fact]
    public void InputSanitizer_ValidatesBranchNames_AgainstInjection()
    {
        // Valid branch names
        Assert.Equal("feature/add-login", InputSanitizer.ValidateBranchName("feature/add-login"));
        Assert.Equal("bugfix/issue-123_test.v1", InputSanitizer.ValidateBranchName("bugfix/issue-123_test.v1"));
        Assert.Equal("main", InputSanitizer.ValidateBranchName("main"));

        // Malicious branch names with command injection characters
        Assert.Throws<ArgumentException>(() => InputSanitizer.ValidateBranchName("feature; rm -rf /"));
        Assert.Throws<ArgumentException>(() => InputSanitizer.ValidateBranchName("test && whoami"));
        Assert.Throws<ArgumentException>(() => InputSanitizer.ValidateBranchName("branch | cat /etc/passwd"));
        Assert.Throws<ArgumentException>(() => InputSanitizer.ValidateBranchName("`reboot`"));
        Assert.Throws<ArgumentException>(() => InputSanitizer.ValidateBranchName("$(whoami)"));
        Assert.Throws<ArgumentException>(() => InputSanitizer.ValidateBranchName("../../../traversal"));
        Assert.Throws<ArgumentException>(() => InputSanitizer.ValidateBranchName(""));
    }

    [Fact]
    public void InputSanitizer_ValidatesCommands_AgainstWhitelist()
    {
        // Allowed commands
        Assert.Equal("dotnet", InputSanitizer.ValidateCommand("dotnet"));
        Assert.Equal("npm", InputSanitizer.ValidateCommand("npm"));
        Assert.Equal("git", InputSanitizer.ValidateCommand("git"));
        Assert.Equal("pytest", InputSanitizer.ValidateCommand("pytest"));
        Assert.Equal("flake8", InputSanitizer.ValidateCommand("flake8"));

        // Blocked unapproved or dangerous commands
        Assert.Throws<UnauthorizedAccessException>(() => InputSanitizer.ValidateCommand("cmd.exe"));
        Assert.Throws<UnauthorizedAccessException>(() => InputSanitizer.ValidateCommand("powershell"));
        Assert.Throws<UnauthorizedAccessException>(() => InputSanitizer.ValidateCommand("bash"));
        Assert.Throws<UnauthorizedAccessException>(() => InputSanitizer.ValidateCommand("curl"));
        Assert.Throws<UnauthorizedAccessException>(() => InputSanitizer.ValidateCommand("wget"));
        Assert.Throws<UnauthorizedAccessException>(() => InputSanitizer.ValidateCommand("rm"));
    }

    [Fact]
    public async Task TerminalTools_BlocksUnauthorizedCommands()
    {
        var tools = new TerminalTools(_testDir);

        // Direct blocked command
        string result1 = await tools.RunCommand("powershell", "-Command Get-Process");
        Assert.Contains("ERROR", result1);
        Assert.Contains("not in the allowed execution whitelist", result1);

        // Chained malicious command
        string result2 = await tools.RunCommand("dotnet", "test && powershell -c calc");
        Assert.Contains("ERROR", result2);
        Assert.Contains("not in the allowed execution whitelist", result2);
    }

    [Fact]
    public async Task GitTools_RejectsMaliciousBranchNames()
    {
        var tools = new GitTools(_testDir);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            tools.CheckoutNewBranch("malicious; whoami"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            tools.PushBranch("branch && rm -rf /"));
    }
}
