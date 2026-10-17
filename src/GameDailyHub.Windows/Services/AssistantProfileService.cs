using System.Text.Json;
using GameDailyHub.Core.Models;

namespace GameDailyHub.Windows.Services;

/// <summary>Machine-independent automation settings of one game's assistant.</summary>
public sealed class AssistantProfile
{
    public const string FileKind = "GameDailyHub.AssistantProfile";
    public string Kind { get; set; } = FileKind;
    public int SchemaVersion { get; set; } = 1;
    public string AdapterId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public AssistantRunMode ToolRunMode { get; set; }
    public TriggerMode Trigger { get; set; }
    public string Arguments { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public CompletionMode Completion { get; set; }
    public string StartPattern { get; set; } = "";
    public string SuccessPattern { get; set; } = "";
    public string FailurePattern { get; set; } = "";
    public double TimeoutMinutes { get; set; } = 60;
    public int RetryCount { get; set; }
    public bool ContinueOnFailure { get; set; } = true;
}

/// <summary>
/// Exports and imports assistant automation settings so they can be reused on another machine.
/// Install paths, working directories, log paths and the source directory are local to a machine
/// and are neither exported nor overwritten on import.
/// </summary>
public static class AssistantProfileService
{
    public static string Export(GameConfig game)
    {
        var daily = game.Daily;
        return JsonSerializer.Serialize(new AssistantProfile
        {
            AdapterId = game.AdapterId,
            ToolName = game.ToolName,
            ToolRunMode = game.ToolRunMode,
            Trigger = daily.Trigger,
            Arguments = daily.Arguments,
            ProfileName = daily.ProfileName,
            Completion = daily.Completion,
            StartPattern = daily.StartPattern,
            SuccessPattern = daily.SuccessPattern,
            FailurePattern = daily.FailurePattern,
            TimeoutMinutes = daily.TimeoutMinutes,
            RetryCount = daily.RetryCount,
            ContinueOnFailure = daily.ContinueOnFailure
        }, JsonDefaults.Options);
    }

    public static AssistantProfile Read(string json)
    {
        AssistantProfile profile;
        try { profile = JsonSerializer.Deserialize<AssistantProfile>(json, JsonDefaults.Options) ?? throw new InvalidDataException("助手配置为空"); }
        catch (JsonException e) { throw new InvalidDataException("不是有效的助手配置文件：" + e.Message, e); }
        if (profile.Kind != AssistantProfile.FileKind) throw new InvalidDataException("不是 GameDailyHub 助手配置文件（整体配置请在“设置 → 数据与备份”导入）");
        if (profile.SchemaVersion != 1) throw new InvalidDataException("不支持助手配置版本 " + profile.SchemaVersion);
        if (new[] { profile.Arguments, profile.ProfileName, profile.StartPattern, profile.SuccessPattern, profile.FailurePattern, profile.ToolName }.Any(s => s is null))
            throw new InvalidDataException("助手配置包含无效的空值");
        if (!Enum.IsDefined(profile.ToolRunMode) || !Enum.IsDefined(profile.Trigger) || !Enum.IsDefined(profile.Completion)) throw new InvalidDataException("枚举配置无效");
        if (!double.IsFinite(profile.TimeoutMinutes) || profile.TimeoutMinutes <= 0 || profile.TimeoutMinutes > 1440 || profile.RetryCount is < 0 or > 10)
            throw new InvalidDataException("超时须在 0–1440 分钟内，重试须在 0–10 次内");
        if (profile.ToolRunMode == AssistantRunMode.Source && !SourceAssistantService.Supports(profile.AdapterId)) throw new InvalidDataException("该助手暂不支持源码运行");
        return profile;
    }

    /// <summary>Applies the file to <paramref name="game"/>; throws before changing anything when it does not fit.</summary>
    public static void Import(string json, GameConfig game)
    {
        var profile = Read(json);
        if (profile.AdapterId != game.AdapterId) throw new InvalidDataException($"助手类型不匹配：文件为 {profile.AdapterId}，当前游戏使用 {game.AdapterId}");
        var daily = game.Daily;
        game.ToolRunMode = profile.ToolRunMode;
        daily.Trigger = profile.Trigger; daily.Arguments = profile.Arguments; daily.ProfileName = profile.ProfileName; daily.Completion = profile.Completion;
        daily.StartPattern = profile.StartPattern; daily.SuccessPattern = profile.SuccessPattern; daily.FailurePattern = profile.FailurePattern;
        daily.TimeoutMinutes = profile.TimeoutMinutes; daily.RetryCount = profile.RetryCount; daily.ContinueOnFailure = profile.ContinueOnFailure;
    }
}
