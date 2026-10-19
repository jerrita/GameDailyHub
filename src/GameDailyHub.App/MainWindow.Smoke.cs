using System.Runtime.InteropServices.WindowsRuntime;
using GameDailyHub.App.Views;
using GameDailyHub.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using global::Windows.Graphics.Imaging;
using global::Windows.Storage.Streams;

namespace GameDailyHub.App;

public sealed partial class MainWindow
{
    internal async Task SmokeAsync(string outputRoot)
    {
        void Mark(string step) => File.AppendAllText(Path.Combine(outputRoot, "steps.log"), step + "\n");
        Mark("Smoke");
        if (!InitializationSucceeded) throw new InvalidOperationException(ViewModel.Message);
        if (!ViewModel.Games.Select(game => game.Config.AdapterId).SequenceEqual(Core.Models.GameCatalog.Games.Select(game => game.AdapterId)))
            throw new InvalidOperationException("支持的游戏未正确载入");
        RootGrid.UpdateLayout();
        await Task.Delay(150);
        var transitionTypes = new List<string>();
        ContentFrame.Navigated += (_, args) => transitionTypes.Add(args.NavigationTransitionInfo?.GetType().Name ?? "None");
        void VerifyNavigation(string page)
        {
            var selected = Navigation.SelectedItem is not null && ReferenceEquals(Navigation.SelectedItem, Navigation.SettingsItem) ? "settings" : (Navigation.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            if (ViewModel.SelectedPage != page || selected != page) throw new InvalidOperationException($"导航历史或选中项错误：预期 {page}，页面 {ViewModel.SelectedPage}，选中项 {selected ?? "空"}");
            if (ContentFrame.CurrentSourcePageType != Pages[page] || ContentFrame.Content is not Page content || !ReferenceEquals(content.DataContext, ViewModel)
                || !ContentFrame.ContentTransitions.OfType<NavigationThemeTransition>().Any())
                throw new InvalidOperationException("原生页面或转场状态错误：" + page);
        }
        TryGoBack(); TryGoForward(); VerifyNavigation("home");
        Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().First(item => item.Tag?.ToString() == "tasks");
        VerifyNavigation("tasks");
        Navigation.SelectedItem = Navigation.SettingsItem;
        VerifyNavigation("settings");
        if (ContentFrame.BackStack.Count != 2 || ContentFrame.ForwardStack.Count != 0) throw new InvalidOperationException("原生导航历史数量错误");
        TryGoBack(); VerifyNavigation("tasks");
        TryGoBack(); TryGoBack(); VerifyNavigation("home");
        TryGoForward(); VerifyNavigation("tasks");
        TryGoForward(); TryGoForward(); VerifyNavigation("settings");
        TryGoBack();
        ViewModel.SelectedPage = "tasks";
        TryGoForward(); VerifyNavigation("settings");
        TryGoBack();
        ViewModel.SelectedPage = "logs";
        TryGoForward(); VerifyNavigation("logs");
        if (ContentFrame.CanGoForward) throw new InvalidOperationException("新导航未清除前进历史");
        TryGoBack(); VerifyNavigation("tasks");
        ViewModel.SelectedPage = "home";
        Mark("Navigation history");
        ViewModel.SelectedGame!.ConfigureCommand.Execute(null);
        if (ViewModel.SelectedPage != "config") throw new InvalidOperationException("卡片配置导航失败");
        var configPage = (GameConfigPage)ContentFrame.Content;
        var AdvancedOptions = (Expander)configPage.FindName("AdvancedOptions");
        var NormalArgumentsEditor = (TextBox)configPage.FindName("NormalArgumentsEditor");
        var DailyArgumentsEditor = (TextBox)configPage.FindName("DailyArgumentsEditor");
        var RetryEditor = (NumberBox)configPage.FindName("RetryEditor");
        var TimeoutEditor = (NumberBox)configPage.FindName("TimeoutEditor");
        var SourceModeEditor = (ComboBox)configPage.FindName("SourceModeEditor");
        var SourceDirectoryEditor = (TextBox)configPage.FindName("SourceDirectoryEditor");
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
        if (ViewModel.AutomaticVisibility != Visibility.Collapsed) throw new InvalidOperationException("手动执行应隐藏自动参数与结果规则");
        ViewModel.SelectedTrigger = Core.Models.TriggerMode.VerifiedCommand;
        ViewModel.SelectedCompletion = Core.Models.CompletionMode.AdapterEvidence;
        ViewModel.SelectedGame = ViewModel.Games.Single(g => g.Config.AdapterId == "maaend");
        if (ViewModel.ProfileVisibility != Visibility.Visible || ViewModel.ProfileHeader != "实例名称（可选）"
            || ViewModel.RunModeVisibility != Visibility.Collapsed || !ViewModel.SelectedGame.AssistantIcon.EndsWith("MaaEnd.png")
            || ViewModel.InstallationVisibility != Visibility.Visible || !ViewModel.CanInstallAssistant
            || !ViewModel.InstallationDirectoryHint.Contains(Path.Combine(ViewModel.DataDirectory, "assistants", "MaaEnd")))
            throw new InvalidOperationException("MaaEnd 配置项或图标错误");
        var installRoot = Path.Combine(ViewModel.DataDirectory, "assistants", "MaaEnd");
        foreach (var relative in new[] { "MaaEnd.exe", "agent/go-service.exe", "agent/cpp-algo.exe", "maafw/MaaFramework.dll", "maafw/MaaToolkit.dll" })
        {
            var path = Path.Combine(installRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "fixture");
        }
        Directory.CreateDirectory(Path.Combine(installRoot, "resource"));
        await File.WriteAllTextAsync(Path.Combine(installRoot, "interface.json"), "{\"name\":\"MaaEnd\"}");
        await ViewModel.InstallAssistantCommand.ExecuteAsync(null);
        var installed = await ConfigurationStore.ReadValidatedAsync(Path.Combine(ViewModel.DataDirectory, "config.json"));
        if (!ViewModel.IsIdle || ViewModel.InstallationBusy || !ViewModel.InstallationStatus.StartsWith("安装完成")
            || ViewModel.SelectedToolPath != Path.Combine(installRoot, "MaaEnd.exe")
            || installed.Games.Single(g => g.AdapterId == "maaend").ToolPath != ViewModel.SelectedToolPath)
            throw new InvalidOperationException("MaaEnd 安装后的路径填充或保存失败：" + ViewModel.Message);
        Mark("MaaEnd installation path round trip");
        ViewModel.SelectedGame = ViewModel.Games[0];
        if (ViewModel.InstallationVisibility != Visibility.Collapsed || ViewModel.CanInstallAssistant) throw new InvalidOperationException("MaaEnd 安装入口应按所选助手显示");
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
        ViewModel.SelectedPage = "tasks";
        TryGoBack();
        VerifyNavigation("config");
        if (!ReferenceEquals(configPage, ContentFrame.Content) || !AdvancedOptions.IsExpanded || NormalArgumentsEditor.Text != "smoke-normal")
            throw new InvalidOperationException("返回游戏页面后未保留编辑状态");
        NormalArgumentsEditor.Text = ""; DailyArgumentsEditor.Text = "";
        RetryEditor.Value = 0; TimeoutEditor.Value = 60;
        ViewModel.SelectedTrigger = Core.Models.TriggerMode.VerifiedCommand;
        ViewModel.SelectedPage = "schedule";
        var ScheduleTimeEditor = (TimePicker)((SchedulePage)ContentFrame.Content).FindName("ScheduleTimeEditor");
        ScheduleTimeEditor.Time = new TimeSpan(9, 35, 0);
        Mark("Edit schedule time");
        await ViewModel.SaveCommand.ExecuteAsync(null);
        persisted = await ConfigurationStore.ReadValidatedAsync(Path.Combine(ViewModel.DataDirectory, "config.json"));
        if (persisted.Schedule.Time != "09:35" || persisted.Schedule.Enabled) throw new InvalidOperationException("时间选择器绑定或默认关闭状态错误");
        AdvancedOptions.IsExpanded = false;
        ViewModel.HasMessage = false;
        ViewModel.SelectedPage = "home";
        var homePage = (HomePage)ContentFrame.Content;
        var HomeLogOutput = (LogOutputView)homePage.FindName("HomeLogOutput");
        var homeScroll = (ScrollViewer)homePage.FindName("PageScroll");
        for (var line = 1; line <= 80; line++) ViewModel.LogLines.Add($"日志滚动检查 {line:000}：用于检查日志区域与滚动位置。");
        RootGrid.UpdateLayout();
        await Task.Delay(150);
        var logScroll = (ScrollViewer)HomeLogOutput.FindName("OutputScroll");
        if (HomeLogOutput.ActualHeight != 230 || logScroll.ScrollableHeight <= 0)
            throw new InvalidOperationException("实时日志应在固定高度内滚动");
        ViewModel.LogLines.Add("日志滚动检查：追加输出");
        await Task.Delay(150);
        if (logScroll.ScrollableHeight - logScroll.VerticalOffset > 2)
            throw new InvalidOperationException("实时日志未跟随最新输出");
        logScroll.ChangeView(null, 40, null, true);
        await Task.Delay(150);
        var readingOffset = logScroll.VerticalOffset;
        ViewModel.LogLines.Add("日志滚动检查：查看历史时追加输出");
        await Task.Delay(150);
        if (Math.Abs(logScroll.VerticalOffset - readingOffset) > 2)
            throw new InvalidOperationException("追加日志改变了历史阅读位置");
        homeScroll.ChangeView(null, homeScroll.ScrollableHeight, null, true);
        await Task.Delay(150);
        var homeOffset = homeScroll.VerticalOffset;
        ViewModel.SelectedPage = "logs";
        RootGrid.UpdateLayout();
        await Task.Delay(150);
        if (((ScrollViewer)((LogsPage)ContentFrame.Content).FindName("PageScroll")).VerticalOffset > 2) throw new InvalidOperationException("切换页面后未回到顶部");
        TryGoBack();
        RootGrid.UpdateLayout();
        await Task.Delay(150);
        if (Math.Abs(homeScroll.VerticalOffset - homeOffset) > 2) throw new InvalidOperationException("后退未保留页面滚动位置");
        TryGoForward();
        foreach (var page in new[] { "home", "tasks", "config", "logs", "schedule", "settings", "source", "zzz-source", "maaend-install" })
        {
            ViewModel.SelectedPage = page is "source" or "zzz-source" or "maaend-install" ? "config" : page;
            if (page is "source" or "zzz-source")
            {
                var index = page == "source" ? 1 : 3;
                ViewModel.SelectedGame = ViewModel.Games[index];
                SourceModeEditor.SelectedItem = Core.Models.AssistantRunMode.Source;
                SourceDirectoryEditor.Text = Path.Combine(outputRoot, page + " checkout");
                await ViewModel.SaveCommand.ExecuteAsync(null);
                persisted = await ConfigurationStore.ReadValidatedAsync(Path.Combine(ViewModel.DataDirectory, "config.json"));
                if (persisted.Games[index].ToolRunMode != Core.Models.AssistantRunMode.Source || persisted.Games[index].ToolSourceDirectory != SourceDirectoryEditor.Text
                    || ViewModel.SourceVisibility != Visibility.Visible || ViewModel.BinaryVisibility != Visibility.Collapsed)
                    throw new InvalidOperationException("源码模式与路径绑定或持久化失败");
                ViewModel.HasMessage = false;
            }
            if (page == "maaend-install") ViewModel.SelectedGame = ViewModel.Games.Single(g => g.Config.AdapterId == "maaend");
            Mark("Render " + page);
            VerifyNavigation(page is "source" or "zzz-source" or "maaend-install" ? "config" : page);
            RootGrid.UpdateLayout();
            await Task.Delay(350);
            if (page == "home" && homeScroll.VerticalOffset > 2) throw new InvalidOperationException("新导航未重置页面滚动位置");
            var paneState = VisualStateManager.GetVisualStateGroups((FrameworkElement)VisualTreeHelper.GetChild(Navigation, 0))
                .First(group => group.Name == "PaneStateListSizeGroup").CurrentState.Name;
            if (Navigation.DisplayMode == NavigationViewDisplayMode.Expanded && Navigation.IsPaneOpen && paneState != "ListSizeFull")
                throw new InvalidOperationException("展开导航栏未显示完整内容：" + paneState);
            var activePage = (Page)ContentFrame.Content;
            var activeScroll = (ScrollViewer)activePage.FindName("PageScroll");
            var panel = (FrameworkElement)((Grid)activeScroll.Content).Children[0];
            var panelPoint = panel.TransformToVisual(RootGrid).TransformPoint(default);
            await File.AppendAllTextAsync(Path.Combine(outputRoot, "layout.jsonl"), System.Text.Json.JsonSerializer.Serialize(new { Page = page, FrameWidth = ContentFrame.ActualWidth, PageWidth = activePage.ActualWidth, ScrollWidth = activeScroll.ActualWidth, PanelWidth = panel.ActualWidth, X = panelPoint.X, Y = panelPoint.Y }) + "\n");
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
        if (transitionTypes.Count == 0 || transitionTypes.Any(type => type is "None" or nameof(SuppressNavigationTransitionInfo)))
            throw new InvalidOperationException("页面切换未使用原生导航过渡");
        Mark("Assistant bundle round trip");
        var selectionDialog = new AssistantSelectionDialog("导出", Core.Models.GameCatalog.Games);
        if (selectionDialog.SelectedAdapterIds.Length != ViewModel.Games.Count || selectionDialog.SelectAll.IsChecked != true)
            throw new InvalidOperationException("助手选择默认全选状态错误");
        selectionDialog.Choices[0].IsChecked = false;
        if (selectionDialog.SelectedAdapterIds.Length != ViewModel.Games.Count - 1 || selectionDialog.SelectAll.IsChecked is not null)
            throw new InvalidOperationException("助手多选状态错误");
        selectionDialog.SelectAll.IsChecked = false;
        if (selectionDialog.IsPrimaryButtonEnabled || selectionDialog.SelectedAdapterIds.Length != 0)
            throw new InvalidOperationException("未选择助手时应禁用操作");
        selectionDialog.SelectAll.IsChecked = true;
        if (selectionDialog.SelectedAdapterIds.Length != ViewModel.Games.Count || !selectionDialog.IsPrimaryButtonEnabled)
            throw new InvalidOperationException("助手全选失败");
        string[] assistantFiles = ["User/config.json", "config.yaml", "configs/daily.json", "config/01/game.yml", "config/mxu-MaaEnd.json"];
        for (var i = 0; i < ViewModel.Games.Count; i++)
        {
            var game = ViewModel.Games[i].Config;
            var root = Path.Combine(outputRoot, "assistant-fixtures", game.AdapterId);
            var full = Path.Combine(root, assistantFiles[i]);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, assistantFiles[i].EndsWith("json") ? "{\"enabled\":true}" : "enabled: true\n");
            game.ToolRunMode = Core.Models.AssistantRunMode.Binary;
            game.ToolPath = Path.Combine(root, "fixture.exe");
            game.ToolGuiPath = "";
            game.Daily.TimeoutMinutes = 20 + i;
        }
        var bundlePath = Path.Combine(outputRoot, "assistants.json");
        var subsetPath = Path.Combine(outputRoot, "selected-assistants.json");
        await ViewModel.ExportAssistantsAsync(subsetPath, ["march7th", "zzz"]);
        if (!File.Exists(subsetPath) || !AssistantBundleService.Read(await File.ReadAllTextAsync(subsetPath)).Assistants.Select(p => p.AdapterId).SequenceEqual(["march7th", "zzz"]))
            throw new InvalidOperationException("勾选助手导出失败：" + ViewModel.Message);
        await ViewModel.ExportAssistantsAsync(bundlePath);
        if (!File.Exists(bundlePath) || AssistantBundleService.Read(await File.ReadAllTextAsync(bundlePath)).Assistants.Count != ViewModel.Games.Count)
            throw new InvalidOperationException("全部助手导出失败：" + ViewModel.Message);
        foreach (var card in ViewModel.Games) card.Config.Daily.TimeoutMinutes = 60;
        await ViewModel.ImportAssistantsAsync(bundlePath, ["march7th", "zzz"]);
        persisted = await ConfigurationStore.ReadValidatedAsync(Path.Combine(ViewModel.DataDirectory, "config.json"));
        if (!persisted.Games.Select(g => g.Daily.TimeoutMinutes).SequenceEqual([60d, 21d, 60d, 23d, 60d]))
            throw new InvalidOperationException("勾选助手导入或持久化失败：" + ViewModel.Message);
        await ViewModel.ImportAssistantsAsync(bundlePath);
        persisted = await ConfigurationStore.ReadValidatedAsync(Path.Combine(ViewModel.DataDirectory, "config.json"));
        if (!ViewModel.IsIdle || persisted.Games.Where((game, i) => game.Daily.TimeoutMinutes != 20 + i).Any())
            throw new InvalidOperationException("全部助手导入或持久化失败：" + ViewModel.Message);
        await File.WriteAllTextAsync(Path.Combine(outputRoot, "smoke.json"), System.Text.Json.JsonSerializer.Serialize(new { Success = true, Pages = 6, Games = ViewModel.Games.Count, NativeFrameNavigation = true, NavigationTransitions = transitionTypes.Distinct().ToArray(), CachedPageState = true, UiConfigurationRoundTrip = true, SourceConfigurationRoundTrip = true, ProgressiveOptions = true, ScheduleTimeRoundTrip = true, StartupAvailable = ViewModel.StartupAvailable, Elevated = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator), NavigationOpen = Navigation.IsPaneOpen, NavigationMode = Navigation.DisplayMode.ToString(), BindingErrors = App.BindingErrors, Timestamp = DateTimeOffset.Now }));
    }
}
