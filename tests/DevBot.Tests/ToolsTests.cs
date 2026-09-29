using DevBot.Cli.Core;
using DevBot.Cli.Core.Planning;
using DevBot.Cli.Core.Strategies;
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
}
