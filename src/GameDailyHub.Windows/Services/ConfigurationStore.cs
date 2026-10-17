using System.Text.Json;
using GameDailyHub.Core.Models;

namespace GameDailyHub.Windows.Services;

public sealed class ConfigurationStore(string root)
{
    private readonly SemaphoreSlim _gate = new(1);
    public string Root { get; } = root;
    public string ConfigPath => Path.Combine(Root, "config.json");
    public string? RecoveryMessage { get; private set; }
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameDailyHub");

    public async Task<AppConfig> LoadAsync()
    {
        Directory.CreateDirectory(Root);
        if (!File.Exists(ConfigPath)) return ApplySuggestions(AppConfig.CreateDefault());
        try { return await ReadValidatedAsync(ConfigPath); }
        catch (Exception e) when (e is JsonException or InvalidDataException or IOException)
        {
            var damaged = ConfigPath + ".damaged-" + DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff");
            File.Copy(ConfigPath, damaged, false);
            if (File.Exists(ConfigPath + ".bak"))
            {
                try
                {
                    var backup = await ReadValidatedAsync(ConfigPath + ".bak");
                    File.Copy(ConfigPath + ".bak", ConfigPath, true);
                    RecoveryMessage = "配置损坏，已从备份恢复。原文件保留为 " + damaged;
                    return backup;
                }
                catch (Exception backupError) when (backupError is JsonException or InvalidDataException or IOException) { }
            }
            RecoveryMessage = "配置与备份无法恢复，已载入默认配置。原文件保留为 " + damaged + "。" + e.Message;
            File.Delete(ConfigPath);
            return ApplySuggestions(AppConfig.CreateDefault());
        }
    }
    public async Task SaveAsync(AppConfig config)
    {
        Validate(config);
        ApplySuggestions(config);
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Root);
            var temp = ConfigPath + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(config, JsonDefaults.Options));
            if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, ConfigPath + ".bak", true);
            else File.Move(temp, ConfigPath);
        }
        finally { _gate.Release(); }
    }
    public async Task<AppConfig> ImportAsync(string path)
    {
        var config = await ReadValidatedAsync(path);
        // Import never enables an unattended plan implicitly.
        config.Schedule.Enabled = false;
        config.Schedule.LastTriggeredDate = null;
        await SaveAsync(config);
        return config;
    }
    public static async Task<AppConfig> ReadValidatedAsync(string path)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(await File.ReadAllTextAsync(path), JsonDefaults.Options)
            ?? throw new InvalidDataException("配置为空");
        Validate(config);
        return ApplySuggestions(config);
    }
    private static AppConfig ApplySuggestions(AppConfig config)
    {
        foreach (var game in config.Games)
        {
            // Persisted normal settings now belong to the game button. Legacy
            // settings for another destination must not become game arguments.
            if (game.Normal.Target != LaunchTarget.Game)
            {
                game.Normal.Target = LaunchTarget.Game;
                game.Normal.Arguments = "";
                game.Normal.WorkingDirectory = "";
            }
            ConfigurationSuggestions.Apply(game);
        }
        return config;
    }
    public static void Validate(AppConfig config)
    {
        if (config.SchemaVersion != 1) throw new InvalidDataException("不支持配置版本 " + config.SchemaVersion);
        if (config.Games is null || config.Tasks is null || config.Schedule is null) throw new InvalidDataException("配置缺少必需字段");
        if (config.Games.Any(g => g is null) || config.Tasks.Any(t => t is null)) throw new InvalidDataException("游戏和任务列表不能包含空对象");
        if (config.Games.Select(g => g.Id).Distinct().Count() != config.Games.Count || config.Tasks.Select(t => t.Id).Distinct().Count() != config.Tasks.Count)
            throw new InvalidDataException("游戏或任务 ID 重复");
        foreach (var g in config.Games)
        {
            if (g.Id == Guid.Empty || string.IsNullOrWhiteSpace(g.Name) || g.Normal is null || g.Daily is null || !new[] { "bettergi", "march7th", "okww", "generic" }.Contains(g.AdapterId))
                throw new InvalidDataException("游戏信息不完整或适配器无效");
            if (new[] { g.GamePath, g.LauncherPath, g.ToolPath, g.ToolGuiPath, g.ToolConfigPath, g.Normal.Arguments, g.Normal.WorkingDirectory, g.Daily.Arguments, g.Daily.ProfileName, g.Daily.LogPath, g.Daily.WorkingDirectory, g.Daily.VerifiedVersion, g.Daily.VerifiedFileStamp }.Any(s => s is null))
                throw new InvalidDataException("路径、参数及核验字段须为字符串");
            if (!double.IsFinite(g.Daily.TimeoutMinutes) || g.Daily.TimeoutMinutes <= 0 || g.Daily.TimeoutMinutes > 1440 || g.Daily.RetryCount is < 0 or > 10)
                throw new InvalidDataException(g.Name + "：超时须在 0–1440 分钟内，重试须在 0–10 次内");
            if (!Enum.IsDefined(g.Normal.Target) || !Enum.IsDefined(g.Daily.Trigger) || !Enum.IsDefined(g.Daily.Completion)) throw new InvalidDataException("枚举配置无效");
        }
        if (config.Tasks.Any(t => t.Id == Guid.Empty || !config.Games.Any(g => g.Id == t.GameId))) throw new InvalidDataException("任务引用了不存在的游戏");
        if (!TimeOnly.TryParseExact(config.Schedule.Time, "HH:mm", out _)) throw new InvalidDataException("计划时间格式应为 HH:mm");
        try { TimeZoneInfo.FindSystemTimeZoneById(config.Schedule.TimeZoneId); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new InvalidDataException("计划时区无效", e); }
    }
}
