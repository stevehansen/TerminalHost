using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class TabDropdownView : UserControl
{
    public TabDropdownView()
    {
        InitializeComponent();
        Loaded += TabDropdownView_Loaded;
        this.GetObservable(IsVisibleProperty).Subscribe(OnIsVisibleChanged);
    }

    private void TabDropdownView_Loaded(object? sender, RoutedEventArgs e)
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
            DropdownSearchBox.Focus();
        }, DispatcherPriority.Input);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not MainViewModel mainViewModel) return;

        if (e.Key == Key.Down)
        {
            if (DropdownTabList.SelectedIndex < DropdownTabList.ItemCount - 1)
            {
                DropdownTabList.SelectedIndex++;
                DropdownTabList.ScrollIntoView(DropdownTabList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (DropdownTabList.SelectedIndex > 0)
            {
                DropdownTabList.SelectedIndex--;
                DropdownTabList.ScrollIntoView(DropdownTabList.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SelectDropdownItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            mainViewModel.IsTabDropdownOpen = false;
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.IsTabDropdownOpen = false;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // Check if the release happened over the list box
        var listBoxBounds = DropdownTabList.Bounds;
        var position = e.GetPosition(DropdownTabList);

        if (listBoxBounds.Contains(position))
        {
            // Single click to select
            SelectDropdownItem();
        }
    }

    private void SelectDropdownItem()
    {
        if (DataContext is not MainViewModel mainViewModel) return;

        if (DropdownTabList.SelectedItem is ITabViewModel tab)
        {
            mainViewModel.SelectedTab = tab;
            mainViewModel.IsTabDropdownOpen = false;
        }
    }
}
