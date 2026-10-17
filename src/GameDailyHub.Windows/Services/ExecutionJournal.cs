using System.Text.Json;
using GameDailyHub.Core.Adapters;
using GameDailyHub.Core.Models;

namespace GameDailyHub.Windows.Services;

public sealed class ExecutionJournal(string root, string? logDirectory = null) : IExecutionJournal
{
    private readonly object _sync = new();
    public string LogDirectory { get; } = string.IsNullOrWhiteSpace(logDirectory) ? Path.Combine(root, "logs") : Path.GetFullPath(logDirectory);
    public string HistoryDirectory { get; } = Path.Combine(root, "history");
    public event Action<LogEntry>? EntryAdded;
    public string? LastStorageError { get; private set; }
    public void Log(LogEntry entry)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(Path.Combine(LogDirectory, $"hub-{entry.Timestamp:yyyy-MM-dd}.jsonl"), JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LastStorageError = e.Message; }
        }
        EntryAdded?.Invoke(entry);
    }
    public Task SaveRunAsync(RunRecord record, CancellationToken cancellationToken = default) => SaveAsync("runs", record.RunId, record, cancellationToken);
    public Task SaveBatchAsync(BatchResult result, CancellationToken cancellationToken = default) => SaveAsync("batches", result.BatchId, result, cancellationToken);
    private async Task SaveAsync<T>(string folder, Guid id, T value, CancellationToken token)
    {
        var dir = Path.Combine(HistoryDirectory, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id + ".json");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(value, JsonDefaults.Options), token);
        File.Move(path + ".tmp", path, true);
    }
    public async Task<IReadOnlyList<RunRecord>> ReadRunsAsync(Guid? game = null, Guid? batch = null)
    {
        var dir = Path.Combine(HistoryDirectory, "runs");
        if (!Directory.Exists(dir)) return [];
        var records = new List<RunRecord>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var r = JsonSerializer.Deserialize<RunRecord>(await File.ReadAllTextAsync(file), JsonDefaults.Options);
                if (r is not null && (game is null || game == r.GameId) && (batch is null || batch == r.BatchId)) records.Add(r);
            }
            catch (Exception e) when (e is IOException or JsonException) { Log(new(DateTimeOffset.Now, "警告", "历史记录读取失败：" + file)); }
        }
        return records.OrderByDescending(x => x.StartedAt).ToArray();
    }
    public async Task<BatchResult?> ReadLastBatchAsync()
    {
        var dir = Path.Combine(HistoryDirectory, "batches");
        if (!Directory.Exists(dir)) return null;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try { return JsonSerializer.Deserialize<BatchResult>(await File.ReadAllTextAsync(file), JsonDefaults.Options); }
            catch (Exception e) when (e is IOException or JsonException) { }
        }
        return null;
    }
    public async Task RecoverInterruptedRunsAsync()
    {
        foreach (var run in await ReadRunsAsync())
            if (run.State is RunState.Starting or RunState.Running)
                await SaveRunAsync(run with { State = RunState.Unknown, FinishedAt = DateTimeOffset.Now, Message = "上次运行未记录终态，可能被中断；请检查助手与任务结果" });
    }
}
