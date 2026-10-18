using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameDailyHub.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LaunchTarget { Game, Launcher, Assistant }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerMode { VerifiedCommand, CustomCommand, Manual }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompletionMode { AdapterEvidence, LogRules, Manual }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssistantRunMode { Binary, Source }

public sealed class NormalLaunchProfile
{
    public LaunchTarget Target { get; set; } = LaunchTarget.Game;
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
}

public sealed class DailyTaskProfile
{
    public TriggerMode Trigger { get; set; } = TriggerMode.VerifiedCommand;
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public CompletionMode Completion { get; set; } = CompletionMode.AdapterEvidence;
    public string LogPath { get; set; } = "";
    public string StartPattern { get; set; } = "";
    public string SuccessPattern { get; set; } = "";
    public string FailurePattern { get; set; } = "";
    public double TimeoutMinutes { get; set; } = 60;
    public int RetryCount { get; set; }
    public bool ContinueOnFailure { get; set; } = true;
}

public sealed class GameConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string AdapterId { get; set; } = "";
    public string GamePath { get; set; } = "";
    public string LauncherPath { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string ToolPath { get; set; } = "";
    public string ToolGuiPath { get; set; } = "";
    public string ToolConfigPath { get; set; } = "";
    public AssistantRunMode ToolRunMode { get; set; } = AssistantRunMode.Binary;
    public string ToolSourceDirectory { get; set; } = "";
    public NormalLaunchProfile Normal { get; set; } = new();
    public DailyTaskProfile Daily { get; set; } = new();
    [JsonIgnore]
    public string NormalPath => Normal.Target switch
    {
        LaunchTarget.Game => GamePath,
        LaunchTarget.Launcher => LauncherPath,
        _ => string.IsNullOrWhiteSpace(ToolGuiPath) ? ToolPath : ToolGuiPath
    };
}

public sealed class QueueTaskConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public string Name { get; set; } = "日常任务";
    public bool Enabled { get; set; } = true;
}

public sealed record SupportedGame(string AdapterId, string Name, string ToolName)
{
    public GameConfig Create() => new() { AdapterId = AdapterId, Name = Name, ToolName = ToolName };
}

public static class GameCatalog
{
    public static IReadOnlyList<SupportedGame> Games { get; } = Array.AsReadOnly<SupportedGame>([
        new("bettergi", "原神", "BetterGI"),
        new("march7th", "崩坏：星穹铁道", "March7thAssistant"),
        new("okww", "鸣潮", "OK-WW"),
        new("zzz", "绝区零", "绝区零一条龙")
    ]);
    public static bool Supports(string adapterId) => Games.Any(game => game.AdapterId == adapterId);
}

public sealed class ScheduleConfig
{
    public bool Enabled { get; set; }
    public string Time { get; set; } = "08:00";
    public string TimeZoneId { get; set; } = "China Standard Time";
    public string? LastTriggeredDate { get; set; }
}

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    public List<GameConfig> Games { get; set; } = [];
    public List<QueueTaskConfig> Tasks { get; set; } = [];
    public ScheduleConfig Schedule { get; set; } = new();
    public string LogDirectory { get; set; } = "";
    public string Theme { get; set; } = "Default";

    public static AppConfig CreateDefault()
    {
        var config = new AppConfig();
        config.Games = GameCatalog.Games.Select(game => game.Create()).ToList();
        config.Tasks = config.Games.Select(g => new QueueTaskConfig { GameId = g.Id, Name = g.Name + " · 日常" }).ToList();
        return config;
    }

    public AppConfig Clone() => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, JsonDefaults.Options), JsonDefaults.Options)!;
}

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}
