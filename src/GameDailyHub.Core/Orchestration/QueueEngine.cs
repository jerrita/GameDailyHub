using System.Text.Json;
using GameDailyHub.Core.Adapters;
using GameDailyHub.Core.Models;

namespace GameDailyHub.Core.Orchestration;

public sealed class QueueEngine(IAdapterRegistry adapters, IExecutionJournal journal)
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _sync = new();
    private CancellationTokenSource? _batchCancellation;
    private CancellationTokenSource? _attemptCancellation;
    private TaskCompletionSource<ManualDecision>? _confirmation;
    private bool _skip;
    public ExecutionSnapshot Snapshot { get; private set; } = ExecutionSnapshot.Idle;
    public event Action<ExecutionSnapshot>? Changed;
    public event Action<BatchResult>? BatchFinished;

    public void Stop()
    {
        lock (_sync) { _batchCancellation?.Cancel(); }
    }
    public void SkipCurrent()
    {
        lock (_sync)
        {
            if (_attemptCancellation is null) return;
            _skip = true;
            _attemptCancellation.Cancel();
        }
    }
    public bool Confirm(ManualDecision decision)
    {
        if (!decision.ConfirmedStopped || decision.State is not (RunState.Success or RunState.Failed or RunState.Skipped)) return false;
        lock (_sync) return _confirmation?.TrySetResult(decision) == true;
    }

    public async Task<BatchResult> RunAsync(IEnumerable<TaskRequest> requests, CancellationToken token = default)
    {
        if (!await _gate.WaitAsync(0, token)) throw new InvalidOperationException("已有一条龙批次正在运行");
        var batchId = Guid.NewGuid();
        var start = DateTimeOffset.Now;
        var records = new List<RunRecord>();
        var states = new Dictionary<Guid, RunState>();
        var tasks = new List<TaskRequest>();
        TaskRequest? current = null;
        DateTimeOffset? currentStart = null;
        void Publish(RunState state, string message)
        {
            Snapshot = new(true, batchId, current?.Game.Id, current?.Game.Name ?? "", current?.Task.Name ?? "", state, currentStart,
                tasks.Count, states.Count(x => x.Value == RunState.Success),
                states.Count(x => x.Value is RunState.Failed or RunState.TimedOut or RunState.Unknown),
                states.Count(x => x.Value == RunState.Skipped), tasks.Count - states.Count, message);
            Changed?.Invoke(Snapshot);
        }
        void Log(string level, string message, Guid? run = null) => journal.Log(new(DateTimeOffset.Now, level, message, batchId, current?.Game.Id, run));
        try
        {
            // Copy every profile before the first await: edits affect future batches only.
            tasks = requests.Select(r => new TaskRequest(
                JsonSerializer.Deserialize<QueueTaskConfig>(JsonSerializer.Serialize(r.Task))!,
                JsonSerializer.Deserialize<GameConfig>(JsonSerializer.Serialize(r.Game, JsonDefaults.Options), JsonDefaults.Options)!)).ToList();
            if (tasks.Select(t => t.Task.Id).Distinct().Count() != tasks.Count) throw new InvalidOperationException("批次含重复任务 ID");
            using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            lock (_sync) _batchCancellation = batchCts;
            Publish(RunState.Validating, "正在校验整个队列");
            foreach (var task in tasks)
            {
                var validation = await adapters.Get(task.Game.AdapterId).ValidateAsync(task.Game, true, batchCts.Token);
                if (!validation.IsValid) throw new InvalidOperationException(task.Game.Name + "：" + string.Join("；", validation.Errors));
            }
            Log("信息", "批次开始，共 " + tasks.Count + " 项");
            foreach (var task in tasks)
            {
                if (batchCts.IsCancellationRequested) break;
                current = task;
                var adapter = adapters.Get(task.Game.AdapterId);
                for (var attempt = 0; attempt <= task.Game.Daily.RetryCount; attempt++)
                {
                    if (batchCts.IsCancellationRequested) break;
                    var runId = Guid.NewGuid();
                    currentStart = DateTimeOffset.Now;
                    AdapterRun? run = null;
                    AdapterResult result;
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(task.Game.Daily.TimeoutMinutes));
                    using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(batchCts.Token, timeoutCts.Token);
                    lock (_sync) { _skip = false; _attemptCancellation = attemptCts; }
                    Publish(RunState.Starting, $"第 {attempt + 1} 次尝试");
                    Log("信息", "触发日常：" + task.Task.Name, runId);
                    await journal.SaveRunAsync(new(batchId, task.Task.Id, task.Game.Id, runId, task.Game.Name, task.Task.Name,
                        attempt + 1, currentStart.Value, currentStart.Value, RunState.Starting, "本次运行尚未取得终态", null, ""));
                    try
                    {
                        // Trigger must clean up on cancellation before returning ownership.
                        run = await adapter.TriggerDailyAsync(task.Game, runId, message => Log("工具", message, runId), attemptCts.Token);
                        attemptCts.Token.ThrowIfCancellationRequested();
                        Publish(RunState.Running, "等待助手的实际任务结果");
                        result = await adapter.WaitForCompletionAsync(run, attemptCts.Token).WaitAsync(attemptCts.Token);
                        attemptCts.Token.ThrowIfCancellationRequested();
                        if (result.State is not (RunState.Success or RunState.Failed or RunState.Unknown))
                            result = new(RunState.Unknown, "适配器没有返回有效终态：" + result.Message, false);
                        if (result.State == RunState.Success && !result.IsQuiescent)
                            result = result with { State = RunState.Unknown, Message = "尚未确认自动化停止：" + result.Message };
                    }
                    catch (OperationCanceledException)
                    {
                        result = new(batchCts.IsCancellationRequested ? RunState.Cancelled : _skip ? RunState.Skipped : RunState.TimedOut,
                            batchCts.IsCancellationRequested ? "用户停止批次" : _skip ? "用户跳过当前任务" : "任务超过配置时限", run is null);
                    }
                    catch (Exception e) { result = new(RunState.Failed, e.Message, run is null); }

                    // Manual mode leaves the assistant open while the user performs the task.
                    if (result.State == RunState.Unknown && !batchCts.IsCancellationRequested)
                    {
                        var pending = new TaskCompletionSource<ManualDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
                        lock (_sync) _confirmation = pending;
                        Publish(RunState.AwaitingConfirmation, result.Message + "。确认自动化已停止后选择结果");
                        Log("警告", "等待人工确认：" + result.Message, runId);
                        try
                        {
                            var decision = await pending.Task.WaitAsync(attemptCts.Token);
                            result = result with { State = decision.State, Evidence = result.Evidence + "\n用户确认：" + decision.State };
                        }
                        catch (OperationCanceledException)
                        {
                            result = result with
                            {
                                State = batchCts.IsCancellationRequested ? RunState.Cancelled : _skip ? RunState.Skipped : RunState.TimedOut,
                                Message = batchCts.IsCancellationRequested ? "用户停止批次" : _skip ? "用户跳过当前任务" : "等待人工确认超过任务时限"
                            };
                        }
                        finally { lock (_sync) _confirmation = null; }
                    }

                    if (run is not null)
                    {
                        try
                        {
                            // End every owned automation process before advancing to another game.
                            // Pre-existing games and normal launches are outside this job.
                            // Cleanup cannot inherit the already-cancelled attempt token.
                            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                            var stopped = await adapter.RequestStopAsync(run, cleanupCts.Token).WaitAsync(cleanupCts.Token);
                            result = result with { IsQuiescent = stopped.IsQuiescent, Message = result.Message + "；" + stopped.Message };
                        }
                        catch (Exception e) { result = result with { IsQuiescent = false, Message = result.Message + "；清理失败：" + e.Message }; }
                        finally
                        {
                            try { await run.DisposeAsync(); }
                            catch (Exception e) { result = result with { IsQuiescent = false, Message = result.Message + "；释放失败：" + e.Message }; }
                        }
                    }

                    if (!result.IsQuiescent)
                    {
                        var pending = new TaskCompletionSource<ManualDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
                        lock (_sync) _confirmation = pending;
                        Publish(RunState.AwaitingConfirmation, result.Message + "。请检查日志并确认任务已停止");
                        Log("警告", "等待人工确认：" + result.Message, runId);
                        ManualDecision decision;
                        if (result.IsQuiescent)
                        {
                            try { decision = await pending.Task.WaitAsync(batchCts.Token); }
                            catch (OperationCanceledException) { decision = new(RunState.Failed, true); result = result with { State = RunState.Cancelled }; }
                        }
                        else decision = await pending.Task; // Keep the automation gate until safety is confirmed.
                        lock (_sync) _confirmation = null;
                        if (!batchCts.IsCancellationRequested) result = result with { State = decision.State, IsQuiescent = true, Evidence = result.Evidence + "\n用户确认：" + decision.State };
                    }
                    if (batchCts.IsCancellationRequested) result = result with { State = RunState.Cancelled };
                    var record = new RunRecord(batchId, task.Task.Id, task.Game.Id, runId, task.Game.Name, task.Task.Name,
                        attempt + 1, currentStart.Value, DateTimeOffset.Now, result.State, result.Message, result.ExitCode, result.Evidence);
                    records.Add(record);
                    await journal.SaveRunAsync(record);
                    Log(result.State == RunState.Success ? "信息" : "警告", result.State.Chinese() + "：" + result.Message, runId);
                    lock (_sync) _attemptCancellation = null;
                    if (result.State is RunState.Failed or RunState.TimedOut && attempt < task.Game.Daily.RetryCount && !batchCts.IsCancellationRequested)
                    {
                        Log("信息", "准备重试", runId);
                        continue;
                    }
                    states[task.Task.Id] = result.State;
                    Publish(result.State, result.Message);
                    if (result.State is RunState.Failed or RunState.TimedOut && !task.Game.Daily.ContinueOnFailure) batchCts.Cancel();
                    break;
                }
            }
            foreach (var task in tasks.Where(t => !states.ContainsKey(t.Task.Id))) states[task.Task.Id] = RunState.Cancelled;
            var batch = new BatchResult(batchId, start, DateTimeOffset.Now, records, states);
            await journal.SaveBatchAsync(batch);
            BatchFinished?.Invoke(batch);
            return batch;
        }
        finally
        {
            lock (_sync) { _batchCancellation = null; _attemptCancellation = null; _confirmation = null; }
            Snapshot = Snapshot with { IsBusy = false, CurrentStartedAt = null, Remaining = 0 };
            _gate.Release();
            Changed?.Invoke(Snapshot);
        }
    }
}
