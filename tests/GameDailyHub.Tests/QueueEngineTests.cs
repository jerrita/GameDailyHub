using System.Collections.Concurrent;
using GameDailyHub.Core.Adapters;
using GameDailyHub.Core.Models;
using GameDailyHub.Core.Orchestration;
using GameDailyHub.Core.Services;
using Xunit;

namespace GameDailyHub.Tests;

internal sealed class MemoryJournal : IExecutionJournal
{
    public event Action<LogEntry>? EntryAdded;
    public ConcurrentBag<RunRecord> Records { get; } = [];
    public void Log(LogEntry entry) => EntryAdded?.Invoke(entry);
    public Task SaveRunAsync(RunRecord record, CancellationToken cancellationToken = default) { Records.Add(record); return Task.CompletedTask; }
    public Task SaveBatchAsync(BatchResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
internal sealed class FakeRun(Guid id, GameConfig game) : AdapterRun(id, game)
{
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
internal sealed class FakeAdapter : IToolAdapter, IAdapterRegistry
{
    public AdapterCapabilities Capabilities { get; } = new("fake", "test", "", true);
    public int Launches, Triggers, Active, MaximumActive, Stops;
    public string NormalArgs = "", DailyArgs = "";
    public string NormalPath = "", NormalWorkingDirectory = "";
    public bool Invalid;
    public Func<AdapterRun, CancellationToken, Task<AdapterResult>> Wait { get; set; } = (_, _) => Task.FromResult(new AdapterResult(RunState.Success, "完成", true));
    public Func<Task>? OnTrigger;
    public bool ThrowOnTrigger;
    public bool CanStop = true;
    public IToolAdapter Get(string id) => this;
    public Task<ValidationResult> ValidateAsync(GameConfig game, bool daily, CancellationToken cancellationToken) => Task.FromResult(Invalid ? new ValidationResult(["无效配置"]) : ValidationResult.Valid);
    public Task<LaunchResult> LaunchNormalAsync(GameConfig game, CancellationToken cancellationToken) { Launches++; NormalArgs = game.Normal.Arguments; NormalPath = game.NormalPath; NormalWorkingDirectory = game.Normal.WorkingDirectory; return Task.FromResult(new LaunchResult(true, false, "已启动")); }
    public async Task<AdapterRun> TriggerDailyAsync(GameConfig game, Guid runId, Action<string> log, CancellationToken cancellationToken)
    {
        Triggers++; DailyArgs = game.Daily.Arguments;
        if (ThrowOnTrigger) throw new IOException("March7th 启动失败");
        Active++; MaximumActive = Math.Max(MaximumActive, Active);
        if (OnTrigger is not null) await OnTrigger();
        return new FakeRun(runId, game);
    }
    public Task<RunState> QueryStatusAsync(AdapterRun run, CancellationToken cancellationToken) => Task.FromResult(RunState.Running);
    public Task<AdapterResult> WaitForCompletionAsync(AdapterRun run, CancellationToken cancellationToken) => Wait(run, cancellationToken);
    public Task<StopResult> RequestStopAsync(AdapterRun run, CancellationToken cancellationToken) { Stops++; Active--; return Task.FromResult(new StopResult(CanStop, "停止")); }
}

public sealed class QueueEngineTests
{
    private static TaskRequest Request(double timeout = 1, int retries = 0, bool keepGoing = true)
    {
        var game = new GameConfig { Name = "测试游戏", Daily = new() { TimeoutMinutes = timeout, RetryCount = retries, ContinueOnFailure = keepGoing } };
        return new(new() { GameId = game.Id, Name = "测试任务" }, game);
    }
    private static async Task UntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(5000);
        while (!condition()) await Task.Delay(5, cts.Token);
    }
    [Fact]
    public async Task NormalLaunchNeverTriggersDaily()
    {
        var adapter = new FakeAdapter(); var launch = new NormalLaunchService(adapter, new MemoryJournal());
        var game = Request().Game; game.Normal.Arguments = "normal"; game.Daily.Arguments = "daily";
        await launch.LaunchAsync(game);
        Assert.Equal(1, adapter.Launches); Assert.Equal(0, adapter.Triggers); Assert.Equal("normal", game.Normal.Arguments);
    }
    [Theory]
    [InlineData(LaunchTarget.Game, "game.exe")]
    [InlineData(LaunchTarget.Assistant, "assistant-gui.exe")]
    public async Task ExplicitLaunchSelectsItsOwnTargetAndPreservesSavedSettings(LaunchTarget target, string expectedPath)
    {
        var adapter = new FakeAdapter();
        var launch = new NormalLaunchService(adapter, new MemoryJournal());
        var game = new GameConfig
        {
            GamePath = "game.exe",
            LauncherPath = "launcher.exe",
            ToolPath = "assistant-task.exe",
            ToolGuiPath = "assistant-gui.exe",
            Normal = new() { Target = LaunchTarget.Launcher, Arguments = "launcher-only", WorkingDirectory = "launcher-directory" }
        };
        var result = await launch.LaunchAsync(game, target);
        Assert.True(result.Success);
        Assert.Equal(expectedPath, adapter.NormalPath);
        Assert.Empty(adapter.NormalArgs);
        Assert.Empty(adapter.NormalWorkingDirectory);
        Assert.Equal(0, adapter.Triggers);
        Assert.Equal(LaunchTarget.Launcher, game.Normal.Target);
        Assert.Equal("launcher-only", game.Normal.Arguments);
        Assert.Equal("launcher-directory", game.Normal.WorkingDirectory);
    }
    [Fact]
    public async Task GameLaunchKeepsGameArgumentsAndAssistantLaunchUsesEmptyArguments()
    {
        var adapter = new FakeAdapter();
        var launch = new NormalLaunchService(adapter, new MemoryJournal());
        var game = new GameConfig
        {
            GamePath = "game.exe",
            ToolPath = "assistant.exe",
            Normal = new() { Arguments = "game-only", WorkingDirectory = "game-directory" }
        };
        await launch.LaunchAsync(game, LaunchTarget.Game);
        Assert.Equal("game-only", adapter.NormalArgs);
        Assert.Equal("game-directory", adapter.NormalWorkingDirectory);
        game.Normal.Target = LaunchTarget.Assistant;
        game.Normal.Arguments = "--startOneDragon";
        await launch.LaunchAsync(game, LaunchTarget.Assistant);
        Assert.Equal("assistant.exe", adapter.NormalPath);
        Assert.Empty(adapter.NormalArgs);
        Assert.Empty(adapter.NormalWorkingDirectory);
        Assert.Equal(0, adapter.Triggers);
        Assert.Equal("--startOneDragon", game.Normal.Arguments);
    }
    [Fact]
    public async Task QueueWaitsForTerminalAndCleanupBeforeNextGame()
    {
        var first = new TaskCompletionSource<AdapterResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAdapter(); adapter.Wait = (_, ct) => adapter.Triggers == 1 ? first.Task.WaitAsync(ct) : Task.FromResult(new AdapterResult(RunState.Success, "ok", true));
        var engine = new QueueEngine(adapter, new MemoryJournal());
        var running = engine.RunAsync([Request(), Request()]); await UntilAsync(() => adapter.Triggers == 1);
        Assert.False(running.IsCompleted); Assert.Equal(1, adapter.Triggers);
        first.SetResult(new(RunState.Success, "ok", true));
        var result = await running;
        Assert.Equal(2, result.SuccessCount); Assert.Equal(1, adapter.MaximumActive); Assert.Equal(2, adapter.Stops);
    }
    [Fact]
    public async Task OnlyOneGlobalBatchMayRun()
    {
        var adapter = new FakeAdapter { Wait = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null!; } };
        var engine = new QueueEngine(adapter, new MemoryJournal()); var running = engine.RunAsync([Request()]);
        await UntilAsync(() => adapter.Triggers == 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunAsync([Request()]));
        engine.Stop(); Assert.Equal(1, (await running).CancelledCount);
    }
    [Fact]
    public async Task TimeoutRetriesAndStopsOwnedAttempt()
    {
        var adapter = new FakeAdapter { Wait = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null!; } };
        var engine = new QueueEngine(adapter, new MemoryJournal());
        var result = await engine.RunAsync([Request(0.0005, 1)]);
        Assert.Equal(2, adapter.Triggers); Assert.Equal(2, adapter.Stops); Assert.Equal(2, result.Attempts.Count);
        Assert.All(result.Attempts, r => Assert.Equal(RunState.TimedOut, r.State));
    }
    [Fact]
    public async Task SkipCancelsCurrentAndContinues()
    {
        var adapter = new FakeAdapter(); adapter.Wait = async (_, ct) =>
        {
            if (adapter.Triggers == 1) await Task.Delay(Timeout.Infinite, ct);
            return new(RunState.Success, "ok", true);
        };
        var engine = new QueueEngine(adapter, new MemoryJournal()); var running = engine.RunAsync([Request(), Request()]);
        await UntilAsync(() => adapter.Triggers == 1); engine.SkipCurrent(); var result = await running;
        Assert.Equal(1, result.SkippedCount); Assert.Equal(1, result.SuccessCount); Assert.Equal(1, adapter.MaximumActive);
    }
    [Fact]
    public async Task UnknownPausesUntilExplicitStoppedConfirmation()
    {
        var adapter = new FakeAdapter { Wait = (_, _) => Task.FromResult(new AdapterResult(RunState.Unknown, "缺少证据", false)) };
        var engine = new QueueEngine(adapter, new MemoryJournal()); var running = engine.RunAsync([Request()]);
        await UntilAsync(() => engine.Snapshot.State == RunState.AwaitingConfirmation);
        Assert.False(running.IsCompleted); Assert.False(engine.Confirm(new(RunState.Success, false))); Assert.Equal(0, adapter.Stops);
        Assert.True(engine.Confirm(new(RunState.Failed, true)));
        Assert.Equal(1, (await running).FailedCount);
    }
    [Fact]
    public async Task StopWhileAwaitingConfirmationCleansUpAndEndsBatch()
    {
        var adapter = new FakeAdapter { Wait = (_, _) => Task.FromResult(new AdapterResult(RunState.Unknown, "人工", false)) };
        var engine = new QueueEngine(adapter, new MemoryJournal()); var running = engine.RunAsync([Request(), Request()]);
        await UntilAsync(() => engine.Snapshot.State == RunState.AwaitingConfirmation); engine.Stop();
        var result = await running; Assert.Equal(2, result.CancelledCount); Assert.Equal(1, adapter.Triggers); Assert.Equal(1, adapter.Stops);
    }
    [Fact]
    public async Task ManualConfirmationStillHonorsAttemptTimeout()
    {
        var adapter = new FakeAdapter { Wait = (_, _) => Task.FromResult(new AdapterResult(RunState.Unknown, "人工", false)) };
        var result = await new QueueEngine(adapter, new MemoryJournal()).RunAsync([Request(0.0005)]);
        Assert.Equal(RunState.TimedOut, Assert.Single(result.Attempts).State); Assert.Equal(1, adapter.Stops);
    }
    [Fact]
    public async Task GlobalSkipAlsoWorksDuringManualConfirmation()
    {
        var adapter = new FakeAdapter(); adapter.Wait = (_, _) => Task.FromResult(new AdapterResult(adapter.Triggers == 1 ? RunState.Unknown : RunState.Success, "result", true));
        var engine = new QueueEngine(adapter, new MemoryJournal()); var running = engine.RunAsync([Request(), Request()]);
        await UntilAsync(() => engine.Snapshot.State == RunState.AwaitingConfirmation); engine.SkipCurrent();
        var result = await running; Assert.Equal(1, result.SkippedCount); Assert.Equal(1, result.SuccessCount);
    }
    [Fact]
    public async Task CleanupFailureHoldsGateUntilHumanConfirmsSafety()
    {
        var adapter = new FakeAdapter { CanStop = false };
        var engine = new QueueEngine(adapter, new MemoryJournal()); var running = engine.RunAsync([Request()]);
        await UntilAsync(() => engine.Snapshot.State == RunState.AwaitingConfirmation);
        engine.Stop(); Assert.False(running.IsCompleted);
        engine.Confirm(new(RunState.Failed, true)); Assert.Equal(1, (await running).CancelledCount);
    }
    [Fact]
    public async Task StartupFailureIsRecordedAndPolicyStopsQueue()
    {
        var adapter = new FakeAdapter { ThrowOnTrigger = true }; var journal = new MemoryJournal();
        var result = await new QueueEngine(adapter, journal).RunAsync([Request(keepGoing: false), Request()]);
        Assert.Equal(1, adapter.Triggers); Assert.Equal(1, result.FailedCount); Assert.Equal(1, result.CancelledCount);
        Assert.Contains("March7th", result.Attempts[0].Message);
    }
    [Fact]
    public async Task PreflightValidatesAllBeforeAnyLaunch()
    {
        var adapter = new FakeAdapter { Invalid = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new QueueEngine(adapter, new MemoryJournal()).RunAsync([Request()]));
        Assert.Equal(0, adapter.Triggers);
    }
    [Fact]
    public async Task ProfilesAreFrozenAndNormalArgumentsRemainIndependent()
    {
        var request = Request(); request.Game.Normal.Arguments = "normal"; request.Game.Daily.Arguments = "old";
        var adapter = new FakeAdapter { OnTrigger = () => { request.Game.Daily.Arguments = "edited"; return Task.CompletedTask; } };
        await new QueueEngine(adapter, new MemoryJournal()).RunAsync([request]);
        Assert.Equal("old", adapter.DailyArgs); Assert.Equal("normal", request.Game.Normal.Arguments);
    }
    [Fact]
    public async Task LateSuccessAfterCancellationCannotStartNextTask()
    {
        var late = new TaskCompletionSource<AdapterResult>();
        var adapter = new FakeAdapter { Wait = (_, _) => late.Task };
        var engine = new QueueEngine(adapter, new MemoryJournal()); var running = engine.RunAsync([Request(), Request()]);
        await UntilAsync(() => adapter.Triggers == 1); engine.Stop(); var result = await running;
        late.SetResult(new(RunState.Success, "too late", true));
        Assert.Equal(1, adapter.Triggers); Assert.Equal(0, result.SuccessCount); Assert.Equal(2, result.CancelledCount);
    }
}
