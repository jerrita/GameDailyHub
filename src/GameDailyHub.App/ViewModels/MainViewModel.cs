using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameDailyHub.Core.Adapters;
using GameDailyHub.Core.Models;
using GameDailyHub.Core.Orchestration;
using GameDailyHub.Core.Services;
using GameDailyHub.Windows.Adapters;
using GameDailyHub.Windows.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GameDailyHub.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcher;
    private readonly ConfigurationStore _store;
    private readonly ProcessService _processes = new();
    private readonly StartupService _startup = new();
    private readonly AdapterRegistry _adapters;
    private readonly SourceAssistantService _sources;
    private CancellationTokenSource? _sourceCancellation;
    private readonly ScheduleEvaluator _schedule = new();
    private readonly List<LogEntry> _logs = [];
    private ExecutionJournal _journal = null!;
    private NormalLaunchService _launch = null!;
    private QueueEngine _engine = null!;
    private AppConfig _config = AppConfig.CreateDefault();
    private AppConfig _savedConfig = AppConfig.CreateDefault();
    private int _processRefreshTicks;
    private string? _lastStorageError;
    private bool _savedStartupEnabled;
    private bool _refreshingOptions;
    public AppConfig Config => _config;
    public ObservableCollection<GameCardViewModel> Games { get; } = [];
    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];
    public ObservableCollection<HistoryRowViewModel> History { get; } = [];
    public IReadOnlyList<string> AdapterIds { get; } = ["bettergi", "march7th", "okww", "generic"];
    public IReadOnlyList<TriggerMode> TriggerModes { get; } = Enum.GetValues<TriggerMode>();
    public IReadOnlyList<CompletionMode> CompletionModes { get; } = Enum.GetValues<CompletionMode>();
    public IReadOnlyList<AssistantRunMode> AssistantRunModes { get; } = Enum.GetValues<AssistantRunMode>();
    public IReadOnlyList<string> Themes { get; } = ["Default", "Light", "Dark"];
    private string _selectedPage = default!;
    public string SelectedPage { get => _selectedPage; set { if (SetProperty(ref _selectedPage, value)) OnSelectedPageChanged(value); } }
    private GameCardViewModel? _selectedGame = default!;
    public GameCardViewModel? SelectedGame { get => _selectedGame; set { if (SetProperty(ref _selectedGame, value)) OnSelectedGameChanged(value); } }
    private TaskRowViewModel? _selectedTask = default!;
    public TaskRowViewModel? SelectedTask { get => _selectedTask; set { SetProperty(ref _selectedTask, value); } }
    private HistoryRowViewModel? _selectedHistory = default!;
    public HistoryRowViewModel? SelectedHistory { get => _selectedHistory; set { if (SetProperty(ref _selectedHistory, value)) OnSelectedHistoryChanged(value); } }
    private string _message = default!;
    public string Message { get => _message; set { SetProperty(ref _message, value); } }
    private bool _hasMessage = default!;
    public bool HasMessage { get => _hasMessage; set { SetProperty(ref _hasMessage, value); } }
    private bool _isBusy = default!;
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) OnIsBusyChanged(value); } }
    private bool _awaitingConfirmation = default!;
    public bool AwaitingConfirmation { get => _awaitingConfirmation; set { if (SetProperty(ref _awaitingConfirmation, value)) OnAwaitingConfirmationChanged(value); } }
    private bool _confirmedStopped = default!;
    public bool ConfirmedStopped { get => _confirmedStopped; set { SetProperty(ref _confirmedStopped, value); } }
    private string _currentTask = default!;
    public string CurrentTask { get => _currentTask; set { SetProperty(ref _currentTask, value); } }
    private string _progressText = default!;
    public string ProgressText { get => _progressText; set { SetProperty(ref _progressText, value); } }
    private double _progress = default!;
    public double Progress { get => _progress; set { SetProperty(ref _progress, value); } }
    private string _elapsed = default!;
    public string Elapsed { get => _elapsed; set { SetProperty(ref _elapsed, value); } }
    private string _lastBatchSummary = default!;
    public string LastBatchSummary { get => _lastBatchSummary; set { SetProperty(ref _lastBatchSummary, value); } }
    private string _batchFilter = default!;
    public string BatchFilter { get => _batchFilter; set { SetProperty(ref _batchFilter, value); } }
    private bool _filterBySelectedGame = default!;
    public bool FilterBySelectedGame { get => _filterBySelectedGame; set { SetProperty(ref _filterBySelectedGame, value); } }
    private bool _startupEnabled = default!;
    public bool StartupEnabled { get => _startupEnabled; set { SetProperty(ref _startupEnabled, value); } }
    public bool StartupAvailable { get; private set; } = true;
    public string StartupHint { get; private set; } = "开关即时保存，以当前用户权限打开应用。";
    public bool IsIdle => !IsBusy && !SourceBusy;
    public bool SourceBusy => _sourceCancellation is not null;
    public Visibility SourceBusyVisibility => SourceBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility HomeVisibility => VisibilityFor("home");
    public Visibility TasksVisibility => VisibilityFor("tasks");
    public Visibility ConfigVisibility => VisibilityFor("config");
    public Visibility LogsVisibility => VisibilityFor("logs");
    public Visibility ScheduleVisibility => VisibilityFor("schedule");
    public Visibility SettingsVisibility => VisibilityFor("settings");
    public Visibility ConfirmationVisibility => AwaitingConfirmation ? Visibility.Visible : Visibility.Collapsed;
    public string DataDirectory => _store.Root;
    public string ActualLogDirectory => _journal?.LogDirectory ?? Path.Combine(_store.Root, "logs");
    public string? SelectedAdapterId
    {
        get => SelectedGame?.Config.AdapterId;
        set
        {
            if (SelectedGame is null || value is null || value == SelectedGame.Config.AdapterId) return;
            SelectedGame.Config.AdapterId = value;
            if (!SourceAssistantService.Supports(value)) SelectedGame.Config.ToolRunMode = AssistantRunMode.Binary;
            SelectedGame.Config.ToolName = value switch { "bettergi" => "BetterGI", "march7th" => "March7thAssistant", "okww" => "OK-WW", _ => "自定义助手" };
            SelectedGame.Config.Daily.Trigger = value == "generic" ? TriggerMode.Manual : TriggerMode.VerifiedCommand;
            SelectedGame.Config.Daily.Completion = value == "generic" ? CompletionMode.Manual : CompletionMode.AdapterEvidence;
            ConfigurationSuggestions.Apply(SelectedGame.Config);
            SelectedGame.Refresh();
            RefreshConfigurationOptions();
        }
    }
    public TriggerMode SelectedTrigger
    {
        get => SelectedGame?.Config.Daily.Trigger ?? TriggerMode.VerifiedCommand;
        set { if (SelectedGame is null || SelectedGame.Config.Daily.Trigger == value) return; SelectedGame.Config.Daily.Trigger = value; RefreshConfigurationOptions(); }
    }
    public CompletionMode SelectedCompletion
    {
        get => SelectedGame?.Config.Daily.Completion ?? CompletionMode.AdapterEvidence;
        set { if (SelectedGame is null || SelectedGame.Config.Daily.Completion == value) return; SelectedGame.Config.Daily.Completion = value; RefreshConfigurationOptions(); }
    }
    public Visibility ProfileVisibility => SelectedGame?.Config.AdapterId is "bettergi" or "march7th" && SelectedTrigger == TriggerMode.VerifiedCommand ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AutomaticVisibility => SelectedTrigger != TriggerMode.Manual ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CustomArgumentsVisibility => SelectedTrigger == TriggerMode.CustomCommand ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LogRulesVisibility => SelectedCompletion == CompletionMode.LogRules ? Visibility.Visible : Visibility.Collapsed;
    public Visibility March7thVisibility => SelectedGame?.Config.AdapterId == "march7th" ? Visibility.Visible : Visibility.Collapsed;
    public string ProfileHeader => SelectedGame?.Config.AdapterId == "bettergi" ? "一条龙配置名称" : "工作流名称（可选）";
    public string ProfileHint => SelectedGame?.Config.AdapterId == "bettergi" ? "填写 BetterGI 中已保存的一条龙名称。" : "留空运行助手默认日常。";
    public string ToolPathHeader => SelectedGame?.Config.AdapterId == "march7th" ? "任务助手 · March7th Assistant.exe" : "任务助手 · " + (SelectedGame?.ToolName ?? "选择 .exe");
    public string ToolPathPlaceholder => SelectedGame?.Config.AdapterId == "generic" ? "选择助手 .exe" : "留空使用默认安装位置";
    public AssistantRunMode SelectedRunMode
    {
        get => SelectedGame?.Config.ToolRunMode ?? AssistantRunMode.Binary;
        set
        {
            if (SelectedGame is null || SelectedRunMode == value || (value == AssistantRunMode.Source && !SourceAssistantService.Supports(SelectedGame.Config.AdapterId))) return;
            SelectedGame.Config.ToolRunMode = value;
            RefreshConfigurationOptions();
        }
    }
    public string SelectedSourceDirectory
    {
        get => SelectedGame?.Config.ToolSourceDirectory ?? "";
        set
        {
            if (SelectedGame is null || SelectedSourceDirectory == value) return;
            SelectedGame.Config.ToolSourceDirectory = value;
            RefreshConfigurationOptions();
        }
    }
    public Visibility RunModeVisibility => SelectedGame is { } game && SourceAssistantService.Supports(game.Config.AdapterId) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SourceVisibility => SelectedRunMode == AssistantRunMode.Source ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BinaryVisibility => SelectedRunMode == AssistantRunMode.Binary ? Visibility.Visible : Visibility.Collapsed;
    public string SourceDirectoryHint => SelectedGame is { } game && SourceAssistantService.Supports(game.Config.AdapterId) ? "源码目录：" + _sources.DirectoryFor(game.Config) : "";
    public Visibility SelectionVisibility => SelectedGame is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EmptySelectionVisibility => SelectedGame is null ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedToolPath
    {
        get => SelectedGame?.Config.ToolPath ?? "";
        set
        {
            if (SelectedGame is not { } game || game.Config.ToolPath == value) return;
            game.Config.ToolPath = value;
            ConfigurationSuggestions.Apply(game.Config);
            RefreshConfigurationOptions();
        }
    }
    public string ExecutionSummary => SelectedTrigger == TriggerMode.Manual ? "在助手内开始任务，完成后回到这里确认结果。" : SelectedGame is { } game
        ? $"每项超时 {game.Config.Daily.TimeoutMinutes:g} 分钟，失败重试 {game.Config.Daily.RetryCount} 次。" + (game.Config.Daily.ContinueOnFailure ? "明确失败后继续下一项。" : "明确失败后停止队列。")
        : "选择游戏后即可设置。";
    public string ScheduleTimeZone => "当前时区：" + _config.Schedule.TimeZoneId;
    public bool ScheduleEnabled
    {
        get => _config.Schedule.Enabled;
        set { if (_config.Schedule.Enabled == value) return; _config.Schedule.Enabled = value; OnPropertyChanged(); }
    }
    public TimeSpan ScheduleTime
    {
        get => TimeOnly.TryParseExact(_config.Schedule.Time, "HH:mm", out var time) ? time.ToTimeSpan() : TimeSpan.FromHours(8);
        set
        {
            if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1)) return;
            var time = TimeOnly.FromTimeSpan(value).ToString("HH:mm");
            if (_config.Schedule.Time == time) return;
            _config.Schedule.Time = time;
            OnPropertyChanged();
        }
    }
    public void RefreshConfigurationOptions()
    {
        if (_refreshingOptions) return;
        _refreshingOptions = true;
        try
        {
            SelectedGame?.Refresh();
            foreach (var name in new[] { nameof(SelectedAdapterId), nameof(SelectedTrigger), nameof(SelectedCompletion), nameof(ProfileVisibility), nameof(AutomaticVisibility), nameof(CustomArgumentsVisibility), nameof(LogRulesVisibility), nameof(March7thVisibility), nameof(ProfileHeader), nameof(ProfileHint), nameof(ToolPathHeader), nameof(ToolPathPlaceholder), nameof(SelectedToolPath), nameof(SelectionVisibility), nameof(EmptySelectionVisibility), nameof(ExecutionSummary) }) OnPropertyChanged(name);
            foreach (var name in new[] { nameof(SelectedRunMode), nameof(SelectedSourceDirectory), nameof(RunModeVisibility), nameof(SourceVisibility), nameof(BinaryVisibility), nameof(SourceDirectoryHint) }) OnPropertyChanged(name);
        }
        finally { _refreshingOptions = false; }
    }
    public string HistoryDetails => SelectedHistory?.Details ?? "选择一条历史记录查看错误与判定证据";
    public IAsyncRelayCommand RunAllCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand RefreshHistoryCommand { get; }
    public IAsyncRelayCommand ApplyStartupCommand { get; }
    public IAsyncRelayCommand UpdateSourceCommand { get; }
    public IAsyncRelayCommand InstallSourceCommand { get; }
    public IAsyncRelayCommand SetupSourceCommand { get; }
    public IRelayCommand CancelSourceCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand SkipCommand { get; }
    public IRelayCommand AddGameCommand { get; }
    public IRelayCommand RemoveGameCommand { get; }
    public IRelayCommand AddTaskCommand { get; }
    public IRelayCommand RemoveTaskCommand { get; }
    public IRelayCommand MoveUpCommand { get; }
    public IRelayCommand MoveDownCommand { get; }
    public IRelayCommand ConfirmSuccessCommand { get; }
    public IRelayCommand ConfirmFailureCommand { get; }
    public IRelayCommand ConfirmSkipCommand { get; }
    public event Action<string>? ThemeRequested;

    public MainViewModel(DispatcherQueue dispatcher, string? dataRoot = null)
    {
        _dispatcher = dispatcher;
        SelectedPage = "home";
        Message = "首次使用：进入游戏设置，选择游戏和助手后保存配置。";
        HasMessage = true;
        CurrentTask = "准备就绪";
        ProgressText = "尚未开始批次";
        Elapsed = "00:00:00";
        LastBatchSummary = "尚无批量执行记录";
        BatchFilter = "";
        _store = new(dataRoot ?? ConfigurationStore.DefaultRoot);
        _sources = new(_processes, _store.Root);
        _adapters = new(_processes, _sources);
        RunAllCommand = new AsyncRelayCommand(() => RunTasksAsync(_config.Tasks.Where(t => t.Enabled).ToArray()));
        SaveCommand = new AsyncRelayCommand(() => GuardAsync(SaveAsync));
        RefreshHistoryCommand = new AsyncRelayCommand(() => GuardAsync(RefreshHistoryAsync));
        UpdateSourceCommand = new AsyncRelayCommand(() => PrepareSourceAsync(true, false, false));
        InstallSourceCommand = new AsyncRelayCommand(() => PrepareSourceAsync(false, true, false));
        SetupSourceCommand = new AsyncRelayCommand(() => PrepareSourceAsync(true, true, true));
        CancelSourceCommand = new RelayCommand(() => _sourceCancellation?.Cancel());
        ApplyStartupCommand = new AsyncRelayCommand(async () =>
        {
            if (!StartupAvailable) { StartupEnabled = _savedStartupEnabled; return; }
            try { await _startup.SetEnabledAsync(StartupEnabled); _savedStartupEnabled = StartupEnabled; Show(StartupEnabled ? "登录时将自动打开 GameDailyHub" : "已关闭登录时启动"); }
            catch (Exception e) { StartupEnabled = _savedStartupEnabled; Show("登录启动设置未能保存：" + e.Message); }
        });
        StopCommand = new RelayCommand(() => _engine.Stop());
        SkipCommand = new RelayCommand(() => _engine.SkipCurrent());
        AddGameCommand = new RelayCommand(AddGame);
        RemoveGameCommand = new RelayCommand(RemoveGame);
        AddTaskCommand = new RelayCommand(AddTask);
        RemoveTaskCommand = new RelayCommand(() => { if (SelectedTask is not null && !IsBusy) { _config.Tasks.Remove(SelectedTask.Config); ReloadTasks(); } });
        MoveUpCommand = new RelayCommand(() => MoveTask(-1));
        MoveDownCommand = new RelayCommand(() => MoveTask(1));
        ConfirmSuccessCommand = new RelayCommand(() => Confirm(RunState.Success));
        ConfirmFailureCommand = new RelayCommand(() => Confirm(RunState.Failed));
        ConfirmSkipCommand = new RelayCommand(() => Confirm(RunState.Skipped));
    }
    public async Task InitializeAsync()
    {
        _config = await _store.LoadAsync();
        _savedConfig = _config.Clone();
        SetServices();
        await _journal.RecoverInterruptedRunsAsync();
        ReloadGames();
        try { _savedStartupEnabled = StartupEnabled = await _startup.IsEnabledAsync(); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            StartupAvailable = false;
            StartupHint = "当前环境限制访问登录启动项。";
        }
        OnPropertyChanged(nameof(StartupAvailable)); OnPropertyChanged(nameof(StartupHint));
        await RefreshHistoryAsync();
        var last = await _journal.ReadLastBatchAsync();
        if (last is not null) SetBatchSummary(last);
        if (_store.RecoveryMessage is not null) Show(_store.RecoveryMessage);
        await _store.SaveAsync(_config);
        ThemeRequested?.Invoke(_config.Theme);
        OnPropertyChanged(nameof(Config));
        OnPropertyChanged(nameof(ScheduleTime)); OnPropertyChanged(nameof(ScheduleTimeZone)); OnPropertyChanged(nameof(ScheduleEnabled));
    }
    private void SetServices()
    {
        _journal = new(_store.Root, _config.LogDirectory);
        _journal.EntryAdded += e => Dispatch(() =>
        {
            _logs.Add(e); if (_logs.Count > 5000) _logs.RemoveAt(0);
            if (MatchesFilter(e)) { LogLines.Add(FormatLog(e)); if (LogLines.Count > 500) LogLines.RemoveAt(0); }
        });
        _launch = new(_adapters, _journal);
        _engine = new(_adapters, _journal);
        _engine.Changed += snapshot => Dispatch(() => ApplySnapshot(snapshot));
        _engine.BatchFinished += result => Dispatch(() => SetBatchSummary(result));
        OnPropertyChanged(nameof(ActualLogDirectory));
    }
    private void Dispatch(Action action) { if (_dispatcher.HasThreadAccess) action(); else _dispatcher.TryEnqueue(() => action()); }
    public void Show(string text) { Message = text; HasMessage = true; }
    private async Task GuardAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { Show(e.Message); _journal?.Log(new(DateTimeOffset.Now, "错误", e.ToString())); }
    }
    private async Task SaveAsync()
    {
        foreach (var game in _config.Games) ConfigurationSuggestions.Apply(game);
        await _store.SaveAsync(_config);
        _savedConfig = _config.Clone();
        foreach (var g in Games) { g.Refresh(); await RefreshCardAsync(g); }
        ReloadTasks();
        RefreshConfigurationOptions();
        OnPropertyChanged(nameof(ScheduleTimeZone));
        ThemeRequested?.Invoke(_config.Theme);
        Show("已保存");
    }
    private async Task PrepareSourceAsync(bool updateCode, bool installEnvironment, bool launch)
    {
        if (!IsIdle || SelectedGame is null || SelectedRunMode != AssistantRunMode.Source) return;
        var game = JsonSerializer.Deserialize<GameConfig>(JsonSerializer.Serialize(SelectedGame.Config, JsonDefaults.Options), JsonDefaults.Options)!;
        using var cancellation = new CancellationTokenSource();
        _sourceCancellation = cancellation;
        SourceStateChanged();
        try
        {
            await SaveAsync();
            Show("正在处理源码助手，进度可在运行日志中查看。");
            await Task.Run(() => _sources.PrepareAsync(game, updateCode, installEnvironment,
                line => _journal.Log(new(DateTimeOffset.Now, "源码", line, GameId: game.Id)), cancellation.Token));
            if (launch)
            {
                var result = await _sources.LaunchGuiAsync(game, cancellation.Token);
                Show(result.Message);
            }
            else Show(installEnvironment ? "源码环境已安装" : "源码已更新，请安装环境后启动");
        }
        catch (OperationCanceledException) { Show("源码操作已取消"); }
        catch (Exception e) { Show(e.Message); _journal.Log(new(DateTimeOffset.Now, "错误", e.Message, GameId: game.Id)); }
        finally
        {
            _sourceCancellation = null;
            SourceStateChanged();
            foreach (var card in Games) await GuardAsync(() => RefreshCardAsync(card));
        }
    }
    private void SourceStateChanged()
    {
        OnPropertyChanged(nameof(SourceBusy)); OnPropertyChanged(nameof(SourceBusyVisibility)); OnPropertyChanged(nameof(IsIdle));
        foreach (var card in Games)
        {
            card.CanLaunch = IsIdle;
            card.CanRun = IsIdle && card.ConfigurationStatus == "日常配置就绪";
        }
    }
    private GameCardViewModel CreateCard(GameConfig game) => new(game,
        (card, target) => GuardAsync(async () =>
        {
            if (SourceBusy) throw new InvalidOperationException("请等待源码操作结束");
            if (IsBusy && _engine.Snapshot.GameId == card.Config.Id) throw new InvalidOperationException("该游戏正在执行日常");
            var config = JsonSerializer.Deserialize<GameConfig>(JsonSerializer.Serialize(card.Config, JsonDefaults.Options), JsonDefaults.Options)!;
            var result = await Task.Run(() => _launch.LaunchAsync(config, target));
            Show(result.Message);
            await RefreshCardAsync(card);
        }),
        card => RunTasksAsync([new QueueTaskConfig { GameId = card.Config.Id, Name = card.Name + " · 单独日常" }]),
        card => { SelectedGame = card; SelectedPage = "config"; });
    private void ReloadGames()
    {
        Games.Clear(); foreach (var g in _config.Games) Games.Add(CreateCard(g));
        SelectedGame = Games.FirstOrDefault(); ReloadTasks();
    }
    private void ReloadTasks()
    {
        Tasks.Clear();
        foreach (var t in _config.Tasks) Tasks.Add(new(t, _config.Games.First(g => g.Id == t.GameId).Name));
    }
    private void AddGame()
    {
        if (IsBusy) return;
        var game = new GameConfig { Daily = new() { Trigger = TriggerMode.Manual, Completion = CompletionMode.Manual } };
        _config.Games.Add(game); var card = CreateCard(game); Games.Add(card); SelectedGame = card; SelectedPage = "config";
    }
    private void RemoveGame()
    {
        if (SelectedGame is null || IsBusy) return;
        var game = SelectedGame.Config;
        _config.Tasks.RemoveAll(t => t.GameId == game.Id); _config.Games.Remove(game); ReloadGames();
        Show("游戏及其队列任务已移除，点击保存配置以持久化。");
    }
    private void AddTask()
    {
        if (SelectedGame is null || IsBusy) return;
        _config.Tasks.Add(new() { GameId = SelectedGame.Config.Id, Name = SelectedGame.Name + " · 日常" }); ReloadTasks();
    }
    private void MoveTask(int direction)
    {
        if (SelectedTask is null || IsBusy) return;
        var index = _config.Tasks.IndexOf(SelectedTask.Config); var next = index + direction;
        if (next < 0 || next >= _config.Tasks.Count) return;
        (_config.Tasks[index], _config.Tasks[next]) = (_config.Tasks[next], _config.Tasks[index]);
        ReloadTasks(); SelectedTask = Tasks[next];
    }
    private async Task RunTasksAsync(IEnumerable<QueueTaskConfig> tasks) => await GuardAsync(async () =>
    {
        if (SourceBusy) throw new InvalidOperationException("请等待源码操作结束");
        var frozen = _config.Clone();
        var requests = tasks.Select(t => new TaskRequest(new() { Id = t.Id, GameId = t.GameId, Name = t.Name, Enabled = t.Enabled }, frozen.Games.First(g => g.Id == t.GameId))).ToArray();
        if (requests.Length == 0) throw new InvalidOperationException("请先启用至少一个队列任务");
        await Task.Run(() => _engine.RunAsync(requests));
        await RefreshHistoryAsync();
    });
    private void ApplySnapshot(ExecutionSnapshot s)
    {
        IsBusy = s.IsBusy; AwaitingConfirmation = s.IsBusy && s.State == RunState.AwaitingConfirmation;
        if (!AwaitingConfirmation) ConfirmedStopped = false;
        CurrentTask = string.IsNullOrEmpty(s.GameName) ? s.Message : $"{s.GameName} · {s.TaskName} · {s.State.Chinese()}";
        ProgressText = $"完成 {s.Completed} / 失败 {s.Failed} / 跳过 {s.Skipped} / 剩余 {s.Remaining} / 总计 {s.Total}";
        Progress = s.Total == 0 ? 0 : 100d * (s.Total - s.Remaining) / s.Total;
        foreach (var g in Games)
        {
            g.CanLaunch = !(s.IsBusy && s.GameId == g.Config.Id);
            g.CanRun = !s.IsBusy && g.ConfigurationStatus == "日常配置就绪";
            if (s.GameId == g.Config.Id) g.DailyStatus = s.State.Chinese();
        }
        if (AwaitingConfirmation) Show(s.Message);
    }
    private void SetBatchSummary(BatchResult result)
    {
        LastBatchSummary = $"{result.FinishedAt:MM-dd HH:mm} · 成功 {result.SuccessCount} / 失败或未知 {result.FailedCount} / 跳过 {result.SkippedCount} / 取消 {result.CancelledCount}";
        foreach (var g in Games)
        {
            var last = result.Attempts.LastOrDefault(r => r.GameId == g.Config.Id);
            if (last is not null) { g.LastRun = $"{last.FinishedAt:MM-dd HH:mm} · {last.State.Chinese()} · {last.Duration.TotalSeconds:F0}秒"; g.DailyStatus = last.State.Chinese(); }
        }
    }
    private void Confirm(RunState state)
    {
        if (!ConfirmedStopped) { Show("请先确认当前自动化已停止"); return; }
        if (_engine.Confirm(new(state, true))) ConfirmedStopped = false;
    }
    private async Task RefreshCardAsync(GameCardViewModel card)
    {
        var validation = await _adapters.Get(card.Config.AdapterId).ValidateAsync(card.Config, true, CancellationToken.None);
        card.ConfigurationStatus = validation.IsValid ? "日常配置就绪" : string.Join("；", validation.Errors);
        card.CanRun = validation.IsValid && IsIdle;
        var running = ProcessPaths(card.Config)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => _processes.FindRunning(p).Count > 0);
        card.ProcessStatus = running ? "运行中" : "未启动";
    }
    private IEnumerable<string> ProcessPaths(GameConfig game) => game.ToolRunMode == AssistantRunMode.Source
        ? [game.GamePath, game.LauncherPath, _sources.PythonPath(game)]
        : [game.GamePath, game.LauncherPath, game.ToolPath, game.ToolGuiPath];
    public async Task TickAsync()
    {
        if (_engine is null) return;
        var started = _engine.Snapshot.CurrentStartedAt;
        Elapsed = started is null ? "00:00:00" : (DateTimeOffset.Now - started.Value).ToString(@"hh\:mm\:ss");
        if (++_processRefreshTicks % 5 == 0)
        {
            foreach (var card in Games.ToArray())
            {
                var paths = ProcessPaths(card.Config).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                try { card.ProcessStatus = await Task.Run(() => paths.Any(path => _processes.FindRunning(path).Count > 0)) ? "运行中" : "未启动"; }
                catch (Exception e) { card.ProcessStatus = "状态未知"; Show(card.Name + "：" + e.Message); }
            }
        }
        if (_journal.LastStorageError is { } storageError && storageError != _lastStorageError)
        {
            _lastStorageError = storageError; Show("日志写入失败，请检查日志目录：" + storageError);
        }
        if (_schedule.ShouldRun(_savedConfig.Schedule, DateTimeOffset.UtcNow))
        {
            await _store.SaveAsync(_savedConfig);
            _config.Schedule.LastTriggeredDate = _savedConfig.Schedule.LastTriggeredDate;
            if (!IsIdle) _journal.Log(new(DateTimeOffset.Now, "警告", "每日计划与当前操作冲突，本日计划已跳过"));
            else
            {
                var saved = _savedConfig.Clone();
                var requests = saved.Tasks.Where(t => t.Enabled).Select(t => new TaskRequest(t, saved.Games.First(g => g.Id == t.GameId))).ToArray();
                _ = GuardAsync(async () => { if (requests.Length == 0) throw new InvalidOperationException("每日计划没有启用的任务"); await Task.Run(() => _engine.RunAsync(requests)); await RefreshHistoryAsync(); });
            }
        }
    }
    private bool MatchesFilter(LogEntry e) => (!FilterBySelectedGame || SelectedGame?.Config.Id == e.GameId) && (string.IsNullOrWhiteSpace(BatchFilter) || e.BatchId?.ToString().Contains(BatchFilter.Trim(), StringComparison.OrdinalIgnoreCase) == true);
    private string FormatLog(LogEntry e) => $"{e.Timestamp:HH:mm:ss} [{e.Level}] {_config.Games.FirstOrDefault(g => g.Id == e.GameId)?.Name} {e.Message}";
    private async Task RefreshHistoryAsync()
    {
        Guid? batch = string.IsNullOrWhiteSpace(BatchFilter) ? null : Guid.TryParse(BatchFilter, out var id) ? id : throw new InvalidOperationException("历史批次筛选请输入完整 GUID");
        var records = await _journal.ReadRunsAsync(FilterBySelectedGame ? SelectedGame?.Config.Id : null, batch);
        History.Clear(); foreach (var r in records) History.Add(new(r));
        foreach (var g in Games)
        {
            var last = records.FirstOrDefault(r => r.GameId == g.Config.Id && r.State is not (RunState.Starting or RunState.Running));
            if (last is not null)
            {
                g.LastRun = $"{last.FinishedAt:MM-dd HH:mm} · {last.State.Chinese()}";
                if (!IsBusy || _engine.Snapshot.GameId != g.Config.Id) g.DailyStatus = last.State.Chinese();
            }
            await RefreshCardAsync(g);
        }
        LogLines.Clear(); foreach (var e in _logs.Where(MatchesFilter).TakeLast(500)) LogLines.Add(FormatLog(e));
    }
    public async Task ExportAsync(string path) => await GuardAsync(async () =>
    {
        ConfigurationStore.Validate(_config);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(_config, JsonDefaults.Options)); Show("配置已导出");
    });
    public async Task ImportAsync(string path) => await GuardAsync(async () =>
    {
        if (!IsIdle) throw new InvalidOperationException("请在当前操作结束后导入配置");
        _config = await _store.ImportAsync(path); _savedConfig = _config.Clone(); SetServices(); ReloadGames(); OnPropertyChanged(nameof(Config));
        OnPropertyChanged(nameof(ScheduleTime)); OnPropertyChanged(nameof(ScheduleTimeZone)); OnPropertyChanged(nameof(ScheduleEnabled));
        await RefreshHistoryAsync(); ThemeRequested?.Invoke(_config.Theme); Show("配置已导入；每日计划保持关闭，请核对后启用。");
    });
    public void RequestStop() { _sourceCancellation?.Cancel(); _engine?.Stop(); }
    private Visibility VisibilityFor(string page) => SelectedPage == page ? Visibility.Visible : Visibility.Collapsed;
    private void OnSelectedPageChanged(string value)
    {
        foreach (var name in new[] { nameof(HomeVisibility), nameof(TasksVisibility), nameof(ConfigVisibility), nameof(LogsVisibility), nameof(ScheduleVisibility), nameof(SettingsVisibility) }) OnPropertyChanged(name);
    }
    private void OnSelectedGameChanged(GameCardViewModel? value) => RefreshConfigurationOptions();
    private void OnSelectedHistoryChanged(HistoryRowViewModel? value) => OnPropertyChanged(nameof(HistoryDetails));
    private void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(BusyVisibility)); }
    private void OnAwaitingConfirmationChanged(bool value) => OnPropertyChanged(nameof(ConfirmationVisibility));
}
