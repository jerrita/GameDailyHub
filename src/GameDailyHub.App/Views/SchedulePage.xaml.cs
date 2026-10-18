using GameDailyHub.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GameDailyHub.App.Views;

public sealed partial class SchedulePage : Page
{
    public MainViewModel ViewModel { get; private set; } = null!;
    private bool _resetScrollOnLoad;

    public SchedulePage()
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
        DataContext = ViewModel;
        Bindings.Update();
        _resetScrollOnLoad = args.NavigationMode == NavigationMode.New;
    }
}
