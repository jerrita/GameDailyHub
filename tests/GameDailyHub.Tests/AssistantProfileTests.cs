using System.Text.Json.Nodes;
using GameDailyHub.Core.Models;
using GameDailyHub.Windows.Services;
using Xunit;

namespace GameDailyHub.Tests;

public sealed class AssistantProfileTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "GameDailyHub-test-" + Guid.NewGuid());
    private string Make(string name) { var d = Path.Combine(_base, name); Directory.CreateDirectory(d); return d; }
    private static void Write(string root, string relative, string text)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, text);
    }
    private static string Read(string root, string relative) => File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    public void Dispose()
    {
        var resolved = Path.GetFullPath(_base);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("GameDailyHub-test-")) throw new InvalidOperationException("测试清理路径越界");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
    private static GameConfig Game(string adapter = "march7th") => new() { AdapterId = adapter };

    [Fact]
    public void ExportCarriesDailySettingsAndImportAppliesThem()
    {
        var source = Game("zzz");
        source.Daily = new() { Trigger = TriggerMode.CustomCommand, Arguments = "--task daily", ProfileName = "flow", Completion = CompletionMode.LogRules, StartPattern = "a", SuccessPattern = "b", FailurePattern = "c", TimeoutMinutes = 30, RetryCount = 2, ContinueOnFailure = false, LogPath = @"C:\source-machine\log.txt", WorkingDirectory = @"C:\source-machine" };
        var json = AssistantProfileService.Export(source, null);
        Assert.DoesNotContain("source-machine", json);

        var target = Game("zzz"); target.Daily.LogPath = @"D:\other\log.txt";
        AssistantProfileService.Import(json, target, null);
        Assert.Equal(("--task daily", "flow", CompletionMode.LogRules), (target.Daily.Arguments, target.Daily.ProfileName, target.Daily.Completion));
        Assert.Equal((30d, 2, false), (target.Daily.TimeoutMinutes, target.Daily.RetryCount, target.Daily.ContinueOnFailure));
        Assert.Equal(@"D:\other\log.txt", target.Daily.LogPath);
    }

    [Fact]
    public void March7thConfigKeepsTargetPathsAndComments()
    {
        var from = Make("from"); var to = Make("to");
        Write(from, "config.yaml", "# my config\ngame_path: C:\\Games\\Source\\StarRail.exe\nauto_update: true\nnotify:\n  webhook: https://example.test/x\n  log_dir: C:\\src\\logs\nextra_paths:\n  - D:\\a\n  - keep-me\n");
        Write(to, "config.yaml", "game_path: D:\\Mine\\StarRail.exe\nauto_update: false\nnotify:\n  log_dir: E:\\mine\\logs\n");
        var json = AssistantProfileService.Export(Game(), from);
        var result = AssistantProfileService.Import(json, Game(), to);

        var merged = Read(to, "config.yaml");
        Assert.Contains("game_path: D:\\Mine\\StarRail.exe", merged);
        Assert.Contains("log_dir: E:\\mine\\logs", merged);
        Assert.Contains("auto_update: true", merged);
        Assert.Contains("webhook: https://example.test/x", merged);
        Assert.Contains("# my config", merged);
        Assert.Contains("keep-me", merged);
        Assert.DoesNotContain("Source", merged);
        Assert.DoesNotContain("D:\\a", merged);
        Assert.Equal(new(1, 1), result);
        Assert.Single(Directory.GetFiles(to, "config.yaml.gdh-*.bak"));
    }

    [Fact]
    public void BetterGiFilesMergeJsonAndDropUnknownLocalPaths()
    {
        var from = Make("from"); var to = Make("to");
        Write(from, "User/config.json", "{\"Theme\":\"dark\",\"Genshin\":{\"InstallPath\":\"C:\\\\Genshin\",\"AutoSkip\":true},\"Dirs\":[\"C:\\\\x\"],\"Names\":[\"a\",\"b\"]}");
        Write(from, "User/OneDragon/日常.json", "{\"Name\":\"日常\"}");
        Write(to, "User/config.json", "{\"Genshin\":{\"InstallPath\":\"D:\\\\Genshin\"},\"Dirs\":[\"D:\\\\y\"]}");
        var json = AssistantProfileService.Export(Game("bettergi"), from);
        AssistantProfileService.Import(json, Game("bettergi"), to);

        var config = JsonNode.Parse(Read(to, "User/config.json"))!;
        Assert.Equal("D:\\Genshin", (string?)config["Genshin"]!["InstallPath"]);
        Assert.True((bool?)config["Genshin"]!["AutoSkip"]);
        Assert.Equal("D:\\y", (string?)config["Dirs"]![0]);
        Assert.Equal(2, config["Names"]!.AsArray().Count);
        Assert.Equal("日常", (string?)JsonNode.Parse(Read(to, "User/OneDragon/日常.json"))!["Name"]);
    }

    [Fact]
    public void ImportWithoutLocalFileDropsForeignPaths()
    {
        var from = Make("from"); var to = Make("to");
        Write(from, "configs/basic.json", "{\"game_exe\":\"C:\\\\Wuwa\\\\Client.exe\",\"level\":3}");
        AssistantProfileService.Import(AssistantProfileService.Export(Game("okww"), from), Game("okww"), to);
        var imported = JsonNode.Parse(Read(to, "configs/basic.json"))!;
        Assert.Null(imported["game_exe"]);
        Assert.Equal(3, (int?)imported["level"]);
    }

    [Fact]
    public void NestedArraysWithNullAndMachinePathsKeepLocalValues()
    {
        var from = Make("from"); var to = Make("to");
        Write(from, "configs/basic.json", "{\"items\":[{\"nested\":[null,{\"path\":\"C:\\\\Source\\\\Client.exe\"}]}]}");
        Write(to, "configs/basic.json", "{\"items\":[{\"nested\":[null,{\"path\":\"D:\\\\Local\\\\Client.exe\"}]}]}");
        AssistantProfileService.Import(AssistantProfileService.Export(Game("okww"), from), Game("okww"), to);
        var result = Read(to, "configs/basic.json");
        Assert.Contains("Local", result);
        Assert.DoesNotContain("Source", result);
    }

    [Fact]
    public void ImportRejectsOtherAssistantAndChangesNothing()
    {
        var from = Make("from"); var to = Make("to");
        Write(from, "config.yaml", "a: 1\n"); Write(to, "config.yaml", "a: 0\n");
        var json = AssistantProfileService.Export(Game(), from);
        Assert.Throws<InvalidDataException>(() => AssistantProfileService.Import(json, Game("okww"), to));
        Assert.Equal("a: 0\n", Read(to, "config.yaml"));
    }

    [Fact]
    public void FailedValidationOfOneFileWritesNothing()
    {
        var from = Make("from"); var to = Make("to");
        Write(from, "configs/a.json", "{\"a\":1}");
        Write(to, "configs/a.json", "{\"a\":0}");
        var json = AssistantProfileService.Export(Game("okww"), from);
        var profile = AssistantProfileService.Read(json);
        profile.Files.Add(new() { Path = "configs/b.json", Content = "{broken" });
        var broken = System.Text.Json.JsonSerializer.Serialize(profile, JsonDefaults.Options);
        Assert.Throws<InvalidDataException>(() => AssistantProfileService.Import(broken, Game("okww"), to));
        Assert.Equal("{\"a\":0}", Read(to, "configs/a.json"));
        Assert.False(File.Exists(Path.Combine(to, "configs", "b.json")));
    }

    [Theory]
    [InlineData("config.yaml", "march7th", true)]
    [InlineData("../config.yaml", "march7th", false)]
    [InlineData("User/OneDragon/x.json", "bettergi", true)]
    [InlineData("User/OneDragon/../../evil.json", "bettergi", false)]
    [InlineData("User/OneDragon/sub/x.json", "bettergi", false)]
    [InlineData("User/Scripts/x.js", "bettergi", false)]
    [InlineData("configs/a.json", "okww", true)]
    [InlineData("C:/Windows/x.json", "okww", false)]
    [InlineData("configs/a.json", "zzz", false)]
    public void OnlyKnownRelativeFilesAreAccepted(string path, string adapter, bool allowed) => Assert.Equal(allowed, AssistantProfileService.IsAllowed(adapter, path));

    [Fact]
    public void ImportRejectsTraversalInFile()
    {
        var to = Make("to");
        var json = AssistantProfileService.Export(Game("zzz"), null);
        var profile = AssistantProfileService.Read(json); profile.AdapterId = "okww";
        profile.Files.Add(new() { Path = "configs/../../evil.json", Content = "{}" });
        Assert.Throws<InvalidDataException>(() => AssistantProfileService.Import(System.Text.Json.JsonSerializer.Serialize(profile, JsonDefaults.Options), Game("okww"), to));
    }

    [Fact]
    public void ExportFailsWhenNothingToExport() => Assert.Throws<InvalidOperationException>(() => AssistantProfileService.Export(Game(), Make("empty")));

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"kind\":\"other\"}")]
    [InlineData("{\"kind\":\"GameDailyHub.AssistantProfile\",\"schemaVersion\":1,\"adapterId\":\"zzz\",\"timeoutMinutes\":0}")]
    [InlineData("{\"kind\":\"GameDailyHub.AssistantProfile\",\"schemaVersion\":1,\"adapterId\":\"zzz\",\"retryCount\":99}")]
    [InlineData("{\"kind\":\"GameDailyHub.AssistantProfile\",\"schemaVersion\":2,\"adapterId\":\"zzz\"}")]
    public void InvalidFilesAreRejected(string json) => Assert.Throws<InvalidDataException>(() => AssistantProfileService.Import(json, Game("zzz"), null));
}
