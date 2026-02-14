using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class TabSwitcherView : UserControl
{
    public TabSwitcherView()
    {
        InitializeComponent();
        AttachedToVisualTree += TabSwitcherView_AttachedToVisualTree;
        this.GetObservable(IsVisibleProperty).Subscribe(OnIsVisibleChanged);
    }

    private void TabSwitcherView_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        FocusSearchBox();
    }

    private void OnIsVisibleChanged(bool isVisible)
    {
        if (isVisible)
            FocusSearchBox();
    }

    private void FocusSearchBox()
    {
        Dispatcher.UIThread.Post(() =>
        {
            SwitcherSearchBox.Focus();
        }, DispatcherPriority.Input);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not MainViewModel mainViewModel) return;

        if (e.Key == Key.Down)
        {
            if (SwitcherTabList.SelectedIndex < SwitcherTabList.ItemCount - 1)
            {
                SwitcherTabList.SelectedIndex++;
                if (SwitcherTabList.SelectedItem != null)
                    SwitcherTabList.ScrollIntoView(SwitcherTabList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (SwitcherTabList.SelectedIndex > 0)
            {
                SwitcherTabList.SelectedIndex--;
                if (SwitcherTabList.SelectedItem != null)
                    SwitcherTabList.ScrollIntoView(SwitcherTabList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SelectSwitcherItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            mainViewModel.IsTabSwitcherOpen = false;
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.IsTabSwitcherOpen = false;
        }
    }

    private void SwitcherTabList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        SelectSwitcherItem();
    }

    private void SelectSwitcherItem()
    {
        if (DataContext is not MainViewModel mainViewModel) return;

        if (SwitcherTabList.SelectedItem is ITabViewModel tab)
        {
            mainViewModel.SelectedTab = tab;
            mainViewModel.IsTabSwitcherOpen = false;
        }
    }
}