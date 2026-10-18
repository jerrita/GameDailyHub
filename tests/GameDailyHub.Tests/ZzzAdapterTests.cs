using GameDailyHub.Core.Models;
using GameDailyHub.Windows.Adapters;
using GameDailyHub.Windows.Services;
using Xunit;

namespace GameDailyHub.Tests;

public sealed class ZzzAdapterTests
{
    [Fact]
    public async Task BinaryDailyValidationUsesTheHubPermissionModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "GameDailyHub-zzz-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "OneDragon-RuntimeLauncher.exe");
            File.WriteAllText(path, "");
            var game = new GameConfig { AdapterId = "zzz", ToolPath = path };
            var result = await new ZzzAdapter(new()).ValidateAsync(game, true, default);
            Assert.True(result.IsValid, string.Join("; ", result.Errors));
        }
        finally { File.Delete(Path.Combine(root, "OneDragon-RuntimeLauncher.exe")); Directory.Delete(root); }
    }

    [Fact]
    public void ChildCompletionCannotFinishDailyAndRootCompletionNeedsConfirmation()
    {
        var monitor = new EvidenceMonitor("zzz", new(), 1);
        monitor.Accept("[INFO]: 指令[ 一条龙 ] 节点 运行应用组 返回状态 下一个");
        monitor.Accept("[INFO]: 指令[ 刮刮卡 ] 执行成功 返回状态 成功");
        Assert.Null(monitor.Evaluate(false, null));
        Assert.Equal(RunState.Unknown, monitor.Evaluate(true, 0)!.State);
        monitor.Accept("[INFO]: 指令[ 一条龙 ] 执行成功 返回状态 全部结束");
        Assert.Null(monitor.Evaluate(false, null));
        var result = monitor.Evaluate(true, 0)!;
        Assert.Equal(RunState.Unknown, result.State);
        Assert.Contains("请核对各子任务", result.Message);
    }

    [Fact]
    public void ChildFailureSurvivesTopLevelCompletion()
    {
        var monitor = new EvidenceMonitor("zzz", new(), 1);
        monitor.Accept("[ERROR]: 指令[ 子任务 ] 执行失败 返回状态 识别失败");
        monitor.Accept("[INFO]: 指令[ 一条龙 ] 执行成功 返回状态 全部结束");
        Assert.Equal(RunState.Failed, monitor.Evaluate(true, 0)!.State);
    }

    [Fact]
    public async Task BinaryCustomDailyMustSelectOneDragonAndOrdinaryLaunchRejectsTaskArguments()
    {
        var game = new GameConfig
        {
            AdapterId = "zzz",
            ToolPath = Path.Combine(AppContext.BaseDirectory, "GameDailyHub.Tests.exe"),
            Daily = new() { Trigger = TriggerMode.CustomCommand, Arguments = "--instance 2" },
            Normal = new() { Target = LaunchTarget.Assistant, Arguments = "--onedragon" }
        };
        var adapter = new ZzzAdapter(new());
        Assert.Contains((await adapter.ValidateAsync(game, true, default)).Errors, e => e.Contains("需要 -o/--onedragon"));
        Assert.Contains((await adapter.ValidateAsync(game, false, default)).Errors, e => e.Contains("仅允许空参数"));
    }
}
