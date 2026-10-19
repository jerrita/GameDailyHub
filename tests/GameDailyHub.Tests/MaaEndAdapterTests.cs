using System.Text.Json;
using System.Text.Json.Nodes;
using GameDailyHub.Core.Models;
using GameDailyHub.Windows.Adapters;
using GameDailyHub.Windows.Services;
using Xunit;

namespace GameDailyHub.Tests;

public sealed class MaaEndAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameDailyHub-maaend-test-" + Guid.NewGuid());
    private readonly GameConfig _game;
    private readonly MaaEndAdapter _adapter = new(new());
    private string ConfigPath => Path.Combine(_root, "config", "mxu-MaaEnd.json");
    private const string Config = """
        {"settings":{"autoStartInstanceId":"daily"},"instances":[{"id":"daily","name":"示例日常","tasks":[{"id":"task","enabled":true}]}]}
        """;

    public MaaEndAdapterTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        _game = GameCatalog.Games.Single(g => g.AdapterId == "maaend").Create();
        _game.ToolPath = Path.Combine(_root, "MaaEnd.exe");
        File.WriteAllText(_game.ToolPath, "");
        File.WriteAllText(Path.Combine(_root, "interface.json"), "{\"name\":\"MaaEnd\"}");
        File.WriteAllText(ConfigPath, Config);
    }

    public void Dispose()
    {
        if (!Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(_root).StartsWith("GameDailyHub-maaend-test-", StringComparison.Ordinal)) throw new InvalidOperationException();
        Directory.Delete(_root, true);
    }

    [Fact]
    public async Task DefaultAndNamedInstancesAreValidatedWithoutFallback()
    {
        Assert.True((await _adapter.ValidateAsync(_game, true, default)).IsValid);
        _game.Daily.ProfileName = "示例日常";
        Assert.True((await _adapter.ValidateAsync(_game, true, default)).IsValid);
        _game.Daily.ProfileName = "missing";
        Assert.Contains((await _adapter.ValidateAsync(_game, true, default)).Errors, e => e.Contains("不存在或重复"));
        var config = JsonNode.Parse(Config)!;
        config["instances"]!.AsArray().Add(config["instances"]![0]!.DeepClone());
        File.WriteAllText(ConfigPath, config.ToJsonString());
        _game.Daily.ProfileName = "示例日常";
        Assert.False((await _adapter.ValidateAsync(_game, true, default)).IsValid);
    }

    [Fact]
    public async Task InitialGuiLaunchWorksAndAutomaticRunRequiresSavedSettings()
    {
        File.Delete(ConfigPath);
        _game.Normal.Target = LaunchTarget.Assistant;
        Assert.True((await _adapter.ValidateAsync(_game, false, default)).IsValid);
        Assert.False((await _adapter.ValidateAsync(_game, true, default)).IsValid);
        _game.Daily.Trigger = TriggerMode.Manual;
        Assert.True((await _adapter.ValidateAsync(_game, true, default)).IsValid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LaunchRejectsAutomaticGuiExecutionAndIndependentSchedules(bool autoRun)
    {
        var config = JsonNode.Parse(Config)!;
        if (autoRun) config["settings"]!["autoRunOnLaunch"] = true;
        else config["instances"]![0]!["schedulePolicies"] = JsonNode.Parse("[{\"enabled\":true}]");
        File.WriteAllText(ConfigPath, config.ToJsonString());
        _game.Normal.Target = LaunchTarget.Assistant;
        Assert.False((await _adapter.ValidateAsync(_game, false, default)).IsValid);
        Assert.False((await _adapter.ValidateAsync(_game, true, default)).IsValid);
    }

    [Theory]
    [InlineData("--autostart -q --instance=示例日常", true)]
    [InlineData("--autostart --quit-after-run -i \"示例日常\"", true)]
    [InlineData("--autostart -q --instance missing", false)]
    [InlineData("--autostart -q -i", false)]
    [InlineData("--autostart -q -i 示例日常 -i 示例日常", false)]
    [InlineData("--autostart --help", false)]
    [InlineData("-q", false)]
    public async Task CustomCommandsMustSelectARealInstanceAndExitAfterRun(string arguments, bool valid)
    {
        _game.Daily.Trigger = TriggerMode.CustomCommand;
        _game.Daily.Arguments = arguments;
        Assert.Equal(valid, (await _adapter.ValidateAsync(_game, true, default)).IsValid);
    }

    [Fact]
    public void ExitAndChildCompletionRequireConfirmationAndFailureWins()
    {
        var monitor = new EvidenceMonitor("maaend", new(), 7);
        monitor.Accept("2026-01-01 08:00:00 INFO  [App] 开机自启动：激活配置并启动任务: 示例日常");
        monitor.Accept("Tasker.Task.Succeeded");
        Assert.Null(monitor.Evaluate(false, null));
        Assert.Equal(RunState.Unknown, monitor.Evaluate(true, 0)!.State);
        monitor.Accept("2026-01-01 08:01:00 INFO  [App] 自动执行任务完成，关闭自身");
        Assert.Null(monitor.Evaluate(false, null));
        Assert.Contains("核对实例", monitor.Evaluate(true, 0)!.Message);
        monitor.Accept("2026-01-01 08:01:00 ERROR [Task] 任务启动异常: 示例错误");
        Assert.Equal(RunState.Failed, monitor.Evaluate(true, 0)!.State);
    }

    [Fact]
    public async Task ExistingFourGameConfigurationGetsMaaEndWithoutChangingQueueChoices()
    {
        var config = AppConfig.CreateDefault();
        var added = config.Games.Single(g => g.AdapterId == "maaend");
        config.Games.Remove(added);
        config.Tasks.RemoveAll(t => t.GameId == added.Id);
        config.Tasks.Reverse();
        config.Tasks[0].Enabled = false;
        var originalTasks = config.Tasks.Select(t => (t.Id, t.GameId, t.Enabled)).ToArray();
        var store = new ConfigurationStore(Path.Combine(_root, "hub"));
        Directory.CreateDirectory(store.Root);
        File.WriteAllText(store.ConfigPath, JsonSerializer.Serialize(config));
        var loaded = await store.LoadAsync();
        Assert.Equal(originalTasks, loaded.Tasks.Take(4).Select(t => (t.Id, t.GameId, t.Enabled)));
        var maaend = Assert.Single(loaded.Games, g => g.AdapterId == "maaend");
        Assert.Single(loaded.Tasks, t => t.GameId == maaend.Id);
        await store.SaveAsync(loaded);
        var again = await store.LoadAsync();
        Assert.Equal(maaend.Id, again.Games.Single(g => g.AdapterId == "maaend").Id);
        Assert.Equal(5, again.Tasks.Count);
    }

    [Fact]
    public void ProfileImportRestoresTasksAndPreservesPathsAcrossReorderedInstances()
    {
        var incoming = JsonNode.Parse(Config)!;
        incoming["instances"]![0]!["savedDevice"] = JsonNode.Parse("{\"connectedProgramPath\":\"C:\\\\Example\\\\Client.exe\"}");
        File.WriteAllText(ConfigPath, incoming.ToJsonString());
        File.WriteAllText(Path.Combine(_root, "config", "unrelated.json"), "{}");
        var exported = AssistantProfileService.Export(_game, _root);
        Assert.Equal("config/mxu-MaaEnd.json", Assert.Single(AssistantProfileService.Read(exported).Files).Path);
        incoming["instances"]![0]!["savedDevice"]!["connectedProgramPath"] = @"D:\Local\Client.exe";
        incoming["instances"]![0]!["tasks"]![0]!["enabled"] = false;
        incoming["instances"]!.AsArray().Insert(0, JsonNode.Parse("{\"id\":\"other\",\"name\":\"其他\"}"));
        File.WriteAllText(ConfigPath, incoming.ToJsonString());
        var result = AssistantProfileService.Import(exported, _game, _root);
        Assert.Equal(1, result.BackupsMade);
        var restored = JsonNode.Parse(File.ReadAllText(ConfigPath))!;
        Assert.True(restored["instances"]![0]!["tasks"]![0]!["enabled"]!.GetValue<bool>());
        Assert.Equal(@"D:\Local\Client.exe", restored["instances"]![0]!["savedDevice"]!["connectedProgramPath"]!.GetValue<string>());
    }
}
