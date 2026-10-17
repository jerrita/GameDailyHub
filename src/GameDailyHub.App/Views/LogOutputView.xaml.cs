using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GameDailyHub.App.Views;

public sealed partial class LogOutputView : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(LogOutputView), new PropertyMetadata("", OnOutputChanged));
    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
        nameof(Lines), typeof(ObservableCollection<string>), typeof(LogOutputView), new PropertyMetadata(null, OnLinesChanged));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public ObservableCollection<string>? Lines { get => (ObservableCollection<string>?)GetValue(LinesProperty); set => SetValue(LinesProperty, value); }
    private bool _refreshPending;

    public LogOutputView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Lines is { } lines)
            {
                lines.CollectionChanged -= LinesChanged;
                lines.CollectionChanged += LinesChanged;
            }
            QueueRefresh();
        };
        Unloaded += (_, _) =>
        {
            if (Lines is { } lines) lines.CollectionChanged -= LinesChanged;
        };
    }
    private static void OnOutputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((LogOutputView)sender).QueueRefresh();
    private static void OnLinesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (LogOutputView)sender;
        if (args.OldValue is ObservableCollection<string> oldLines) oldLines.CollectionChanged -= view.LinesChanged;
        if (view.IsLoaded && args.NewValue is ObservableCollection<string> newLines) newLines.CollectionChanged += view.LinesChanged;
        view.QueueRefresh();
    }
    private void LinesChanged(object? sender, NotifyCollectionChangedEventArgs args) => QueueRefresh();
    private void QueueRefresh()
    {
        if (_refreshPending) return;
        _refreshPending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _refreshPending = false;
            var offset = OutputScroll.VerticalOffset;
            var followTail = OutputScroll.ScrollableHeight - offset <= 2;
            OutputText.Text = Lines is { } lines ? string.Join(Environment.NewLine, lines) : Text;
            OutputScroll.UpdateLayout();
            OutputScroll.ChangeView(null, followTail ? OutputScroll.ScrollableHeight : offset, null, true);
        });
    }
}
