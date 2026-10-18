using System.Diagnostics;
using GameDailyHub.App.ViewModels;
using GameDailyHub.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GameDailyHub.App.Views;

public sealed partial class SettingsPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;
    private bool _resetScrollOnLoad;
    private nint _windowHandle;
    private bool _applyingStartup;
    public SettingsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        Loaded += (_, _) =>
        {
            if (_resetScrollOnLoad) PageScroll.ChangeView(null, 0, null, true);
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        var context = (PageNavigationContext)args.Parameter;
        ViewModel = context.ViewModel;
        _windowHandle = context.WindowHandle;
        DataContext = ViewModel;
        Bindings.Update();
        _resetScrollOnLoad = args.NavigationMode == NavigationMode.New;
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (IsLoaded) ViewModel.ApplyTheme();
    }
    private async void Startup_Toggled(object sender, RoutedEventArgs args)
    {
        if (!IsLoaded || _applyingStartup) return;
        _applyingStartup = true;
        try { ViewModel.StartupEnabled = ((ToggleSwitch)sender).IsOn; await ViewModel.ApplyStartupCommand.ExecuteAsync(null); }
        finally { _applyingStartup = false; }
    }
    private async void Import_Click(object sender, RoutedEventArgs args)
    {
        var path = FileDialogService.Choose(_windowHandle, "JSON 配置\0*.json\0\0");
        if (path is not null) await ViewModel.ImportAsync(path);
    }
    private async void Export_Click(object sender, RoutedEventArgs args)
    {
        var path = FileDialogService.Choose(_windowHandle, "JSON 配置\0*.json\0\0", true);
        if (path is not null) await ViewModel.ExportAsync(path);
    }
    private void OpenLogs_Click(object sender, RoutedEventArgs args)
    {
        try { Directory.CreateDirectory(ViewModel.ActualLogDirectory); Process.Start(new ProcessStartInfo(ViewModel.ActualLogDirectory) { UseShellExecute = true }); }
        catch (Exception e) { ViewModel.Show(e.Message); }
    }

    private async void ImportAssistants_Click(object sender, RoutedEventArgs args)
    {
        var path = FileDialogService.Choose(_windowHandle, "全部助手配置\0*.json\0\0");
        if (path is not null) await ViewModel.ImportAssistantsAsync(path);
    }

    private async void ExportAssistants_Click(object sender, RoutedEventArgs args)
    {
        var path = FileDialogService.Choose(_windowHandle, "全部助手配置\0*.json\0\0", true);
        if (path is not null) await ViewModel.ExportAssistantsAsync(path);
    }
}
