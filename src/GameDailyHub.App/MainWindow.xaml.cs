using System.ComponentModel;
using GameDailyHub.App.ViewModels;
using GameDailyHub.App.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;

namespace GameDailyHub.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["home"] = typeof(HomePage),
        ["tasks"] = typeof(TasksPage),
        ["config"] = typeof(GameConfigPage),
        ["logs"] = typeof(LogsPage),
        ["schedule"] = typeof(SchedulePage),
        ["settings"] = typeof(SettingsPage)
    };
    public MainViewModel ViewModel { get; }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly PageNavigationContext _pageContext;
    private bool _tickBusy, _closingPrompt, _closeReady, _syncingNavigation;
    public bool InitializationSucceeded { get; private set; }

    public MainWindow()
    {
        ViewModel = new(DispatcherQueue, App.SmokeRoot is null ? null : Path.Combine(App.SmokeRoot, "data"));
        InitializeComponent();
        _pageContext = new(ViewModel, WindowNative.GetWindowHandle(this));
        var backKey = new KeyboardAccelerator { Key = global::Windows.System.VirtualKey.GoBack };
        backKey.Invoked += GoBack_Invoked;
        RootGrid.KeyboardAccelerators.Add(backKey);
        var forwardKey = new KeyboardAccelerator { Key = global::Windows.System.VirtualKey.GoForward };
        forwardKey.Invoked += GoForward_Invoked;
        RootGrid.KeyboardAccelerators.Add(forwardKey);
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnNavigationPointerPressed), true);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        RootGrid.DataContext = ViewModel;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        var scale = GetDpiForWindow(_pageContext.WindowHandle) / 96d;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(1440 * scale), (int)(work.Width * 0.95));
        var height = Math.Min((int)(900 * scale), (int)(work.Height * 0.94));
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        RootGrid.Loaded += (_, _) => NavigateToPage(ViewModel.SelectedPage);
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
        Closed += (_, _) =>
        {
            _timer.Stop();
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        };
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

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.SelectedPage) && RootGrid.IsLoaded)
            NavigateToPage(ViewModel.SelectedPage);
    }

    private void NavigateToPage(string page, NavigationTransitionInfo? transition = null)
    {
        if (_syncingNavigation || !Pages.TryGetValue(page, out var pageType) || ContentFrame.CurrentSourcePageType == pageType) return;
        ContentFrame.Navigate(pageType, _pageContext, transition ?? new EntranceNavigationTransitionInfo());
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingNavigation || !RootGrid.IsLoaded) return;
        var page = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
        if (page is not null) NavigateToPage(page, args.RecommendedNavigationTransitionInfo);
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs args)
    {
        var page = Pages.First(entry => entry.Value == args.SourcePageType).Key;
        _syncingNavigation = true;
        try
        {
            ViewModel.SelectedPage = page;
            Navigation.SelectedItem = page == "settings" ? Navigation.SettingsItem
                : Navigation.MenuItems.OfType<NavigationViewItem>().First(item => item.Tag?.ToString() == page);
            Navigation.IsBackEnabled = ContentFrame.CanGoBack;
        }
        finally { _syncingNavigation = false; }
    }

    private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs args)
        => throw new InvalidOperationException("无法打开页面：" + args.SourcePageType.Name, args.Exception);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    private bool TryGoBack()
    {
        if (Navigation.IsPaneOpen && Navigation.DisplayMode is NavigationViewDisplayMode.Compact or NavigationViewDisplayMode.Minimal)
        {
            Navigation.IsPaneOpen = false;
            return true;
        }
        if (!ContentFrame.CanGoBack) return false;
        ContentFrame.GoBack();
        return true;
    }

    private bool TryGoForward()
    {
        if (!ContentFrame.CanGoForward) return false;
        ContentFrame.GoForward();
        return true;
    }

    private void Navigation_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args) => TryGoBack();
    private void GoBack_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => args.Handled = TryGoBack();
    private void GoForward_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => args.Handled = TryGoForward();

    private void OnNavigationPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        switch (args.GetCurrentPoint(RootGrid).Properties.PointerUpdateKind)
        {
            case Microsoft.UI.Input.PointerUpdateKind.XButton1Pressed:
                TryGoBack(); args.Handled = true; break;
            case Microsoft.UI.Input.PointerUpdateKind.XButton2Pressed:
                TryGoForward(); args.Handled = true; break;
        }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeReady) return;
        args.Cancel = true;
        if (_closingPrompt) return;
        _closingPrompt = true;
        try
        {
            if (ViewModel.IsIdle)
            {
                await ViewModel.SaveIfChangedAsync();
                _closeReady = true; Close();
                return;
            }
            var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "任务仍在运行", Content = "停止当前任务后才能关闭应用。", PrimaryButtonText = "停止任务", CloseButtonText = "继续运行", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                ViewModel.RequestStop(); ViewModel.SelectedPage = "home";
                ViewModel.Show("正在停止。如需确认结果，请先完成确认再关闭窗口。");
            }
        }
        finally { _closingPrompt = false; }
    }
}
