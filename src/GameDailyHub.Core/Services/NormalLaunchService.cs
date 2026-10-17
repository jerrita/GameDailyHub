using System.Text.Json;
using GameDailyHub.Core.Adapters;
using GameDailyHub.Core.Models;

namespace GameDailyHub.Core.Services;

public sealed class NormalLaunchService(IAdapterRegistry adapters, IExecutionJournal journal)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    public Task<LaunchResult> LaunchAsync(GameConfig game, LaunchTarget target, CancellationToken token = default)
    {
        var snapshot = JsonSerializer.Deserialize<GameConfig>(JsonSerializer.Serialize(game, JsonDefaults.Options), JsonDefaults.Options)!;
        // Each card button chooses its own destination. Assistant UI launches use
        // empty arguments; parameters saved for another destination stay separate.
        if (target == LaunchTarget.Assistant || target != snapshot.Normal.Target)
        {
            snapshot.Normal.Arguments = "";
            snapshot.Normal.WorkingDirectory = "";
        }
        snapshot.Normal.Target = target;
        return LaunchAsync(snapshot, token);
    }
    public async Task<LaunchResult> LaunchAsync(GameConfig game, CancellationToken token = default)
    {
        var gate = _locks.GetOrAdd(game.Id, _ => new(1));
        if (!await gate.WaitAsync(0, token)) return new(false, false, "该游戏正在处理启动请求");
        try
        {
            var adapter = adapters.Get(game.AdapterId);
            var validation = await adapter.ValidateAsync(game, false, token);
            var result = validation.IsValid ? await adapter.LaunchNormalAsync(game, token)
                : new LaunchResult(false, false, string.Join("；", validation.Errors));
            journal.Log(new(DateTimeOffset.Now, result.Success ? "信息" : "错误", "普通启动：" + result.Message, GameId: game.Id));
            return result;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            journal.Log(new(DateTimeOffset.Now, "错误", "普通启动失败：" + e.Message, GameId: game.Id));
            return new(false, false, e.Message);
        }
        finally { gate.Release(); }
    }

}
