using System.Diagnostics;
using GameDailyHub.App.ViewModels;
using GameDailyHub.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GameDailyHub.App.Views;

public sealed partial class GameConfigPage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;
    private bool _resetScrollOnLoad;
    private nint _windowHandle;
    public GameConfigPage()
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

    private void Browse_Click(object sender, RoutedEventArgs args)
    {
        var game = ViewModel.SelectedGame;
        if (game is null) return;
        var property = ((Button)sender).Tag.ToString()!;
        var filter = property == "ToolConfigPath" ? "配置文件\0*.yaml;*.yml;*.json\0所有文件\0*.*\0\0" : "可执行文件\0*.exe\0\0";
        var path = FileDialogService.Choose(_windowHandle, filter);
        if (path is null) return;
        if (property == "ToolPath")
            ViewModel.SelectedToolPath = path;
        else typeof(Core.Models.GameConfig).GetProperty(property)!.SetValue(game.Config, path);
        // Replace the selection notification so classic bindings re-read the changed path.
        ViewModel.SelectedGame = null; ViewModel.SelectedGame = game; game.Refresh();
    }
    private async void ImportAssistant_Click(object sender, RoutedEventArgs args)
    {
        var path = FileDialogService.Choose(_windowHandle, "助手配置\0*.json\0\0");
        if (path is not null) await ViewModel.ImportAssistantAsync(path);
    }
    private async void ExportAssistant_Click(object sender, RoutedEventArgs args)
    {
        var path = FileDialogService.Choose(_windowHandle, "助手配置\0*.json\0\0", true);
        if (path is not null) await ViewModel.ExportAssistantAsync(path);
    }
    private void OpenSource_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            if (Directory.Exists(ViewModel.SourceDirectory))
                Process.Start(new ProcessStartInfo(ViewModel.SourceDirectory) { UseShellExecute = true });
        }
        catch (Exception e) { ViewModel.Show(e.Message); }
    }
}
