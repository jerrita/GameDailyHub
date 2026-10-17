namespace GameDailyHub.Core.Models;

public enum RunState { Pending, Validating, Starting, Running, AwaitingConfirmation, Success, Failed, TimedOut, Cancelled, Skipped, Unknown }
public sealed record AdapterCapabilities(string Id, string BaselineVersion, string SourceUrl, bool HasAutomaticTrigger);
public sealed record ValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
    public static ValidationResult Valid { get; } = new([]);
}
public sealed record LaunchResult(bool Success, bool Reused, string Message, int? ProcessId = null);
public sealed record AdapterResult(RunState State, string Message, bool IsQuiescent, int? ExitCode = null, string Evidence = "");
public sealed record StopResult(bool IsQuiescent, string Message);
public sealed record ManualDecision(RunState State, bool ConfirmedStopped);
public sealed record TaskRequest(QueueTaskConfig Task, GameConfig Game);
public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message, Guid? BatchId = null, Guid? GameId = null, Guid? RunId = null);
public sealed record RunRecord(Guid BatchId, Guid TaskId, Guid GameId, Guid RunId, string GameName, string TaskName,
    int Attempt, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, RunState State, string Message, int? ExitCode, string Evidence)
{
    public TimeSpan Duration => FinishedAt - StartedAt;
}
public sealed record BatchResult(Guid BatchId, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, IReadOnlyList<RunRecord> Attempts,
    IReadOnlyDictionary<Guid, RunState> FinalStates)
{
    public int SuccessCount => FinalStates.Count(x => x.Value == RunState.Success);
    public int FailedCount => FinalStates.Count(x => x.Value is RunState.Failed or RunState.TimedOut or RunState.Unknown);
    public int SkippedCount => FinalStates.Count(x => x.Value == RunState.Skipped);
    public int CancelledCount => FinalStates.Count(x => x.Value == RunState.Cancelled);
}
public sealed record ExecutionSnapshot(bool IsBusy, Guid? BatchId, Guid? GameId, string GameName, string TaskName,
    RunState State, DateTimeOffset? CurrentStartedAt, int Total, int Completed, int Failed, int Skipped, int Remaining, string Message)
{
    public static ExecutionSnapshot Idle { get; } = new(false, null, null, "", "", RunState.Pending, null, 0, 0, 0, 0, 0, "准备就绪");
}

public static class StateText
{
    public static string Chinese(this RunState state) => state switch
    {
        RunState.Pending => "未执行",
        RunState.Validating => "校验配置",
        RunState.Starting => "正在启动",
        RunState.Running => "执行日常中",
        RunState.AwaitingConfirmation => "等待人工确认",
        RunState.Success => "成功",
        RunState.Failed => "失败",
        RunState.TimedOut => "超时",
        RunState.Cancelled => "已取消",
        RunState.Skipped => "已跳过",
        _ => "结果未知"
    };
}
