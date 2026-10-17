using System.Diagnostics;
using GameDailyHub.App.ViewModels;
using GameDailyHub.Windows.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using global::Windows.Graphics.Imaging;
using global::Windows.Storage.Streams;
using WinRT.Interop;

namespace GameDailyHub.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _tickBusy, _closingPrompt, _applyingStartup;
    public bool InitializationSucceeded { get; private set; }
    public MainWindow()
    {
        ViewModel = new(DispatcherQueue, App.SmokeRoot is null ? null : Path.Combine(App.SmokeRoot, "data"));
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        RootGrid.DataContext = ViewModel;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedPage))
                Navigation.SelectedItem = ViewModel.SelectedPage == "settings" ? Navigation.SettingsItem
                    : Navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => item.Tag?.ToString() == ViewModel.SelectedPage);
            if (e.PropertyName is nameof(MainViewModel.SelectedGame) or nameof(MainViewModel.SelectedToolPath) or nameof(MainViewModel.SelectedAdapterId) && ViewModel.SelectedGame is { } game)
                VerificationExpander.IsExpanded = !game.Config.Daily.SafeSettingsConfirmed || (game.Config.Daily.Trigger != Core.Models.TriggerMode.Manual && !game.Config.Daily.VersionConfirmed);
        };
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1440, 980));
        RootGrid.Loaded += (_, _) =>
        {
            var scale = RootGrid.XamlRoot.RasterizationScale;
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var width = Math.Min((int)(1440 * scale), (int)(work.Width * 0.95));
            var height = Math.Min((int)(900 * scale), (int)(work.Height * 0.94));
            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        };
        ViewModel.ThemeRequested += theme => RootGrid.RequestedTheme = Enum.TryParse<ElementTheme>(theme, out var value) ? value : ElementTheme.Default;
        _timer.Tick += async (_, _) =>
        {
            if (_tickBusy) return;
            _tickBusy = true;
            try { await ViewModel.TickAsync(); }
            catch (Exception e) { ViewModel.Show("刷新或定时计划出错：" + e.Message); }
            finally { _tickBusy = false; }
        };
        AppWindow.Closing += OnClosing;
        Closed += (_, _) => _timer.Stop();
    }
    public async Task InitializeAsync()
    {
        try
        {
            if (App.SmokeRoot is not null) await File.AppendAllTextAsync(Path.Combine(App.SmokeRoot, "steps.log"), "Initialize\n");
            await ViewModel.InitializeAsync(); _timer.Start(); InitializationSucceeded = true;
            if (App.SmokeRoot is not null) await File.AppendAllTextAsync(Path.Combine(App.SmokeRoot, "steps.log"), "Initialized\n");
        }
        catch (Exception e) { ViewModel.Show("初始化失败：" + e); }
    }
    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        ViewModel.SelectedPage = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "home";
    }
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!ViewModel.IsBusy) return;
        args.Cancel = true;
        if (_closingPrompt) return;
        _closingPrompt = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "任务正在运行", Content = "停止当前批次并等待安全清理后，再关闭应用。", PrimaryButtonText = "停止任务", CloseButtonText = "继续运行", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                ViewModel.RequestStop(); ViewModel.SelectedPage = "home";
                ViewModel.Show("正在停止。请等待队列结束；如需人工确认，请处理后再关闭窗口。");
            }
        }
        finally { _closingPrompt = false; }
    }
    private void Browse_Click(object sender, RoutedEventArgs args)
    {
        var game = ViewModel.SelectedGame;
        if (game is null) return;
        var property = ((Button)sender).Tag.ToString()!;
        var filter = property == "ToolConfigPath" ? "配置文件\0*.yaml;*.yml;*.json\0所有文件\0*.*\0\0" : "可执行文件\0*.exe\0\0";
        var path = FileDialogService.Choose(WindowNative.GetWindowHandle(this), filter);
        if (path is null) return;
        if (property == "ToolPath")
            ViewModel.SelectedToolPath = path;
        else typeof(Core.Models.GameConfig).GetProperty(property)!.SetValue(game.Config, path);
        // Replace the selection notification so classic bindings re-read the changed path.
        ViewModel.SelectedGame = null; ViewModel.SelectedGame = game; game.Refresh();
    }
    private void Reverify_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel.SelectedGame is not { } game) return;
        game.Config.Daily.VersionConfirmed = false;
        game.Config.Daily.VerifiedFileStamp = "";
        ViewModel.RefreshConfigurationOptions();
        ViewModel.Show("请核对新版本与入口，勾选确认后保存即可。");
    }
    private async void Startup_Toggled(object sender, RoutedEventArgs args)
    {
        if (!InitializationSucceeded || _applyingStartup) return;
        _applyingStartup = true;
        try { ViewModel.StartupEnabled = ((ToggleSwitch)sender).IsOn; await ViewModel.ApplyStartupCommand.ExecuteAsync(null); }
        finally { _applyingStartup = false; }
    }
    private async void Import_Click(object sender, RoutedEventArgs args)
    {
        var path = FileDialogService.Choose(WindowNative.GetWindowHandle(this), "JSON 配置\0*.json\0\0");
        if (path is not null) await ViewModel.ImportAsync(path);
    }
    private async void Export_Click(object sender, RoutedEventArgs args)
    {
        var path = FileDialogService.Choose(WindowNative.GetWindowHandle(this), "JSON 配置\0*.json\0\0", true);
        if (path is not null) await ViewModel.ExportAsync(path);
    }
    private void OpenLogs_Click(object sender, RoutedEventArgs args)
    {
        try { Directory.CreateDirectory(ViewModel.ActualLogDirectory); Process.Start(new ProcessStartInfo(ViewModel.ActualLogDirectory) { UseShellExecute = true }); }
        catch (Exception e) { ViewModel.Show(e.Message); }
    }
    internal async Task SmokeAsync(string outputRoot)
    {
        void Mark(string step) => File.AppendAllText(Path.Combine(outputRoot, "steps.log"), step + "\n");
        Mark("Smoke");
        if (!InitializationSucceeded) throw new InvalidOperationException(ViewModel.Message);
        if (ViewModel.Games.Count != 3) throw new InvalidOperationException("默认游戏未正确载入");
        ViewModel.SelectedGame!.ConfigureCommand.Execute(null);
        if (ViewModel.SelectedPage != "config") throw new InvalidOperationException("卡片配置导航失败");
        if (AdvancedOptions.IsExpanded) throw new InvalidOperationException("高级设置应默认收起");
        if (ViewModel.ProfileVisibility != Visibility.Visible || ViewModel.CustomArgumentsVisibility != Visibility.Collapsed || ViewModel.LogRulesVisibility != Visibility.Collapsed)
            throw new InvalidOperationException("默认配置显示项不正确");
        ViewModel.SelectedGame = ViewModel.Games[2];
        Mark("Select OK-WW");
        if (ViewModel.ProfileVisibility != Visibility.Collapsed || ViewModel.March7thVisibility != Visibility.Collapsed) throw new InvalidOperationException("OK-WW 应隐藏其他助手选项");
        ViewModel.SelectedTrigger = Core.Models.TriggerMode.CustomCommand;
        ViewModel.SelectedCompletion = Core.Models.CompletionMode.LogRules;
        if (ViewModel.CustomArgumentsVisibility != Visibility.Visible || ViewModel.LogRulesVisibility != Visibility.Visible) throw new InvalidOperationException("自定义设置未按需显示");
        ViewModel.SelectedTrigger = Core.Models.TriggerMode.Manual;
        if (ViewModel.AutomaticVisibility != Visibility.Collapsed) throw new InvalidOperationException("手动执行应隐藏自动核验与结果规则");
        ViewModel.SelectedTrigger = Core.Models.TriggerMode.VerifiedCommand;
        ViewModel.SelectedCompletion = Core.Models.CompletionMode.AdapterEvidence;
        ViewModel.SelectedGame = ViewModel.Games[0];
        Mark("Select BetterGI");
        AdvancedOptions.IsExpanded = true;
        foreach (var options in ((StackPanel)AdvancedOptions.Content).Children.OfType<Expander>()) options.IsExpanded = true;
        ViewModel.SelectedTrigger = Core.Models.TriggerMode.CustomCommand;
        RootGrid.UpdateLayout();
        await Task.Delay(150);
        NormalArgumentsEditor.Text = "smoke-normal";
        DailyArgumentsEditor.Text = "smoke-daily";
        RetryEditor.Value = 2; TimeoutEditor.Value = 2.75;
        await ViewModel.SaveCommand.ExecuteAsync(null);
        Mark("Save custom values");
        var persisted = await ConfigurationStore.ReadValidatedAsync(Path.Combine(ViewModel.DataDirectory, "config.json"));
        if (persisted.Games[0].Normal.Arguments != "smoke-normal" || persisted.Games[0].Daily.Arguments != "smoke-daily") throw new InvalidOperationException("界面配置双向绑定或持久化失败");
        if (persisted.Games[0].Daily.RetryCount != 2 || persisted.Games[0].Daily.TimeoutMinutes != 2.75) throw new InvalidOperationException("数字配置绑定失败");
        NormalArgumentsEditor.Text = ""; DailyArgumentsEditor.Text = "";
        RetryEditor.Value = 0; TimeoutEditor.Value = 60;
        ViewModel.SelectedTrigger = Core.Models.TriggerMode.VerifiedCommand;
        ScheduleTimeEditor.Time = new TimeSpan(9, 35, 0);
        Mark("Edit schedule time");
        await ViewModel.SaveCommand.ExecuteAsync(null);
        persisted = await ConfigurationStore.ReadValidatedAsync(Path.Combine(ViewModel.DataDirectory, "config.json"));
        if (persisted.Schedule.Time != "09:35" || persisted.Schedule.Enabled) throw new InvalidOperationException("时间选择器绑定或默认关闭状态错误");
        AdvancedOptions.IsExpanded = false;
        ViewModel.HasMessage = false;
        foreach (var page in new[] { "home", "tasks", "config", "logs", "schedule", "settings" })
        {
            ViewModel.SelectedPage = page;
            Mark("Render " + page);
            RootGrid.UpdateLayout();
            await Task.Delay(150);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(RootGrid);
            if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidOperationException(page + " 页面未渲染");
            using var memory = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync(); memory.Seek(0);
            using var file = File.Create(Path.Combine(outputRoot, page + ".png"));
            await memory.AsStreamForRead().CopyToAsync(file);
        }
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "smoke.json"), System.Text.Json.JsonSerializer.Serialize(new { Success = true, Pages = 6, Games = 3, UiConfigurationRoundTrip = true, ProgressiveOptions = true, ScheduleTimeRoundTrip = true, StartupAvailable = ViewModel.StartupAvailable, Elevated = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator), NavigationOpen = Navigation.IsPaneOpen, NavigationMode = Navigation.DisplayMode.ToString(), BindingErrors = App.BindingErrors, Timestamp = DateTimeOffset.Now }));
    }
}
