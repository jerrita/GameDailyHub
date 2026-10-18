using GameDailyHub.Core.Models;
using Microsoft.UI.Xaml.Data;

namespace GameDailyHub.App.Views;

public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        LaunchTarget.Game => "游戏客户端",
        LaunchTarget.Launcher => "游戏启动器",
        LaunchTarget.Assistant => "助手",
        TriggerMode.VerifiedCommand => "自动开始",
        TriggerMode.CustomCommand => "自定义命令",
        TriggerMode.Manual => "在助手内手动开始",
        CompletionMode.AdapterEvidence => "自动识别",
        CompletionMode.LogRules => "按日志规则",
        CompletionMode.Manual => "手动确认",
        AssistantRunMode.Binary => "安装版",
        AssistantRunMode.Source => "源码",
        "Default" => "跟随系统",
        "Light" => "浅色",
        "Dark" => "深色",
        "generic" => "自定义助手",
        "bettergi" => "BetterGI",
        "march7th" => "March7thAssistant",
        "okww" => "OK-WW",
        "zzz" => "绝区零一条龙",
        _ => value
    };
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
