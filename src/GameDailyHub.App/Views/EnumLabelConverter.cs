using GameDailyHub.Core.Models;
using Microsoft.UI.Xaml.Data;

namespace GameDailyHub.App.Views;

public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        LaunchTarget.Game => "游戏客户端",
        LaunchTarget.Launcher => "游戏启动器",
        LaunchTarget.Assistant => "自动化助手（仅打开界面）",
        TriggerMode.VerifiedCommand => "通过官方入口自动开始",
        TriggerMode.CustomCommand => "使用自定义命令",
        TriggerMode.Manual => "在助手内手动开始",
        CompletionMode.AdapterEvidence => "自动识别助手结果",
        CompletionMode.LogRules => "使用自定义日志规则",
        CompletionMode.Manual => "由我确认结果",
        AssistantRunMode.Binary => "二进制",
        AssistantRunMode.Source => "源码",
        "Default" => "跟随系统",
        "Light" => "浅色",
        "Dark" => "深色",
        "generic" => "通用配置适配器",
        "bettergi" => "BetterGI",
        "march7th" => "March7thAssistant",
        "okww" => "OK-WW",
        _ => value
    };
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
