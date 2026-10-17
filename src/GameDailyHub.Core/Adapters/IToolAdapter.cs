using GameDailyHub.Core.Models;

namespace GameDailyHub.Core.Adapters;

public abstract class AdapterRun(Guid runId, GameConfig game) : IAsyncDisposable
{
    public Guid RunId { get; } = runId;
    public GameConfig Game { get; } = game;
    public abstract ValueTask DisposeAsync();
}

public interface IToolAdapter
{
    AdapterCapabilities Capabilities { get; }
    Task<ValidationResult> ValidateAsync(GameConfig game, bool daily, CancellationToken cancellationToken);
    Task<LaunchResult> LaunchNormalAsync(GameConfig game, CancellationToken cancellationToken);
    Task<AdapterRun> TriggerDailyAsync(GameConfig game, Guid runId, Action<string> log, CancellationToken cancellationToken);
    Task<RunState> QueryStatusAsync(AdapterRun run, CancellationToken cancellationToken);
    Task<AdapterResult> WaitForCompletionAsync(AdapterRun run, CancellationToken cancellationToken);
    Task<StopResult> RequestStopAsync(AdapterRun run, CancellationToken cancellationToken);
}

public interface IAdapterRegistry { IToolAdapter Get(string id); }
public interface IExecutionJournal
{
    event Action<LogEntry>? EntryAdded;
    void Log(LogEntry entry);
    Task SaveRunAsync(RunRecord record, CancellationToken cancellationToken = default);
    Task SaveBatchAsync(BatchResult result, CancellationToken cancellationToken = default);
}
