using GameDailyHub.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GameDailyHub.App.Views;

internal sealed class AssistantSelectionDialog : ContentDialog
{
    private bool _updating;
    internal CheckBox SelectAll { get; } = new() { Content = "全选" };
    internal IReadOnlyList<CheckBox> Choices { get; }
    internal string[] SelectedAdapterIds => Choices.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToArray();

    internal AssistantSelectionDialog(string action, IEnumerable<SupportedGame> assistants)
    {
        Title = action + "助手配置";
        PrimaryButtonText = action;
        CloseButtonText = "取消";
        DefaultButton = ContentDialogButton.Primary;
        Choices = assistants.Select(a => new CheckBox { Content = a.Name + " · " + a.ToolName, Tag = a.AdapterId, IsChecked = true }).ToArray();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(SelectAll);
        foreach (var choice in Choices)
        {
            panel.Children.Add(choice);
            choice.Checked += (_, _) => UpdateSelection();
            choice.Unchecked += (_, _) => UpdateSelection();
        }
        SelectAll.Checked += (_, _) => SetAll(true);
        SelectAll.Unchecked += (_, _) => SetAll(false);
        Content = panel;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (_updating) return;
        _updating = true;
        var count = Choices.Count(c => c.IsChecked == true);
        SelectAll.IsChecked = count == 0 ? false : count == Choices.Count ? true : null;
        IsPrimaryButtonEnabled = count > 0;
        _updating = false;
    }

    private void SetAll(bool selected)
    {
        if (_updating) return;
        _updating = true;
        foreach (var choice in Choices) choice.IsChecked = selected;
        _updating = false;
        UpdateSelection();
    }
}
