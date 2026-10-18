using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameDailyHub.Core.Models;
using Microsoft.UI.Xaml;

namespace GameDailyHub.App.ViewModels;

public partial class GameCardViewModel : ObservableObject
{
    public GameConfig Config { get; }
    public string Name => Config.Name;
    public string ToolName => Config.ToolName;
    public string Glyph => Config.AdapterId switch { "bettergi" => "✦", "march7th" => "✧", "okww" => "◈", "zzz" => "◎", _ => "◇" };
    public const string ReadyStatus = "已就绪";
    private string _processStatus = default!;
    public string ProcessStatus { get => _processStatus; set { SetProperty(ref _processStatus, value); } }
    private string _dailyStatus = default!;
    public string DailyStatus { get => _dailyStatus; set { SetProperty(ref _dailyStatus, value); } }
    private string _lastRun = default!;
    public string LastRun { get => _lastRun; set { SetProperty(ref _lastRun, value); } }
    private string _configurationStatus = default!;
    public string ConfigurationStatus { get => _configurationStatus; set { if (SetProperty(ref _configurationStatus, value)) { OnPropertyChanged(nameof(SetupHint)); OnPropertyChanged(nameof(SetupHintVisibility)); } } }
    public string SetupHint => ConfigurationStatus == ReadyStatus ? "" : ConfigurationStatus;
    public Visibility SetupHintVisibility => ConfigurationStatus == ReadyStatus ? Visibility.Collapsed : Visibility.Visible;
    private bool _canRun = default!;
    public bool CanRun { get => _canRun; set { SetProperty(ref _canRun, value); } }
    private bool _canLaunch = default!;
    public bool CanLaunch { get => _canLaunch; set { SetProperty(ref _canLaunch, value); } }
    public IAsyncRelayCommand LaunchAssistantCommand { get; }
    public IAsyncRelayCommand LaunchGameCommand { get; }
    public IAsyncRelayCommand RunCommand { get; }
    public IRelayCommand ConfigureCommand { get; }
    public GameCardViewModel(GameConfig config, Func<GameCardViewModel, LaunchTarget, Task> launch, Func<GameCardViewModel, Task> run, Action<GameCardViewModel> configure)
    {
        Config = config;
        ProcessStatus = "未启动";
        DailyStatus = "今日未运行";
        LastRun = "暂无运行记录";
        ConfigurationStatus = "请先完成设置";
        CanLaunch = true;
        LaunchAssistantCommand = new AsyncRelayCommand(() => launch(this, LaunchTarget.Assistant));
        LaunchGameCommand = new AsyncRelayCommand(() => launch(this, LaunchTarget.Game));
        RunCommand = new AsyncRelayCommand(() => run(this));
        ConfigureCommand = new RelayCommand(() => configure(this));
    }
    public void Refresh() => OnPropertyChanged(string.Empty);
}

public sealed class TaskRowViewModel(QueueTaskConfig config, string gameName)
{
    public QueueTaskConfig Config { get; } = config;
    public string GameName { get; } = gameName;
}

public sealed record HistoryRowViewModel(RunRecord Record)
{
    public string Title => $"{Record.StartedAt:MM-dd HH:mm} · {Record.GameName} · {Record.State.Chinese()}";
    public string Subtitle => $"{Record.TaskName} · 第 {Record.Attempt} 次 · {Record.Duration.TotalSeconds:F1} 秒";
    public string Details => $"批次：{Record.BatchId}\n执行：{Record.RunId}\n退出码：{Record.ExitCode?.ToString() ?? "无"}\n结果：{Record.Message}\n\n{Record.Evidence}";
}
