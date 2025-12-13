using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost;

/// <summary>
/// Tab drag-drop, overflow, switcher, and cycling functionality.
/// </summary>
public partial class MainWindow
{
    #region Tab Drag-Drop and Middle-Click

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Middle-click to close tab
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            if (sender is FrameworkElement element && element.DataContext is ITabViewModel tab)
            {
                _viewModel.CloseTabCommand.Execute(tab);
                e.Handled = true;
            }
        }
    }

    private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ITabViewModel tab)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedTab = tab;
        }
    }

    private void Tab_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedTab == null)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        var diff = _dragStartPoint - currentPosition;

        // Check if moved enough to start drag
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            // Create drag data
            var dragData = new DataObject("TabViewModel", _draggedTab);
            DragDrop.DoDragDrop((DependencyObject)sender, dragData, DragDropEffects.Move);

            _draggedTab = null;
        }
    }

    private void Tab_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("TabViewModel"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        // Visual feedback - highlight drop target
        if (sender is Border border)
        {
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4"));
            border.BorderThickness = new Thickness(2, 2, 2, 0);
        }
    }

    private void Tab_DragLeave(object sender, DragEventArgs e)
    {
        // Remove visual feedback
        if (sender is Border border)
        {
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
        }
    }

    private void Tab_Drop(object sender, DragEventArgs e)
    {
        // Remove visual feedback
        if (sender is Border border)
        {
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
        }

        if (!e.Data.GetDataPresent("TabViewModel"))
        {
            return;
        }

        var droppedTab = e.Data.GetData("TabViewModel") as ITabViewModel;
        if (droppedTab == null)
        {
            return;
        }

        // Get target tab
        if (sender is FrameworkElement element && element.DataContext is ITabViewModel targetTab)
        {
            if (droppedTab == targetTab)
            {
                return; // Dropped on itself
            }

            var oldIndex = _viewModel.Tabs.IndexOf(droppedTab);
            var newIndex = _viewModel.Tabs.IndexOf(targetTab);

            if (oldIndex >= 0 && newIndex >= 0)
            {
                _viewModel.Tabs.Move(oldIndex, newIndex);
            }
        }

        e.Handled = true;
    }

    #endregion

    #region Tab Overflow and Switcher

    private ScrollViewer? GetTabListScrollViewer()
    {
        if (TabList.Template?.FindName("ScrollViewer", TabList) is ScrollViewer sv)
            return sv;

        // Try to find it manually
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(TabList); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(TabList, i);
            if (child is ScrollViewer scrollViewer)
                return scrollViewer;
            if (child is Border border && border.Child is ScrollViewer innerSv)
                return innerSv;
        }
        return null;
    }

    private void TabList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateOverflowButtonsVisibility();
    }

    private void UpdateOverflowButtonsVisibility()
    {
        var scrollViewer = GetTabListScrollViewer();
        if (scrollViewer == null)
        {
            // No scroll viewer found, show overflow buttons based on tab count
            var hasOverflow = _viewModel.Tabs.Count > 5;
            ScrollLeftButton.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            ScrollRightButton.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            TabDropdownButton.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var hasHorizontalOverflow = scrollViewer.ExtentWidth > scrollViewer.ViewportWidth;
        ScrollLeftButton.Visibility = hasHorizontalOverflow ? Visibility.Visible : Visibility.Collapsed;
        ScrollRightButton.Visibility = hasHorizontalOverflow ? Visibility.Visible : Visibility.Collapsed;
        TabDropdownButton.Visibility = hasHorizontalOverflow ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        var scrollViewer = GetTabListScrollViewer();
        if (scrollViewer != null)
        {
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - 150);
        }
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        var scrollViewer = GetTabListScrollViewer();
        if (scrollViewer != null)
        {
            scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + 150);
        }
    }

    private void TabDropdown_Click(object sender, RoutedEventArgs e)
    {
        // Populate and show dropdown
        DropdownTabList.ItemsSource = _viewModel.Tabs;
        DropdownTabList.SelectedItem = _viewModel.SelectedTab;
        DropdownSearchBox.Text = "";
        TabDropdownPopup.IsOpen = true;
        DropdownSearchBox.Focus();
    }

    private void DropdownSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = DropdownSearchBox.Text?.ToLower() ?? "";
        if (string.IsNullOrEmpty(searchText))
        {
            DropdownTabList.ItemsSource = _viewModel.Tabs;
        }
        else
        {
            var filtered = _viewModel.Tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText));
            DropdownTabList.ItemsSource = filtered;
        }
    }

    private void DropdownTabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DropdownTabList.SelectedItem is ITabViewModel tab)
        {
            _viewModel.SelectedTab = tab;
            TabDropdownPopup.IsOpen = false;
        }
    }

    #endregion

    #region Tab Switcher (Ctrl+Shift+T)

    private void ShowTabSwitcher()
    {
        // Populate and show switcher
        SwitcherTabList.ItemsSource = _viewModel.Tabs;
        SwitcherTabList.SelectedItem = _viewModel.SelectedTab;
        SwitcherSearchBox.Text = "";
        TabSwitcherPopup.IsOpen = true;
        SwitcherSearchBox.Focus();
    }

    private void SwitcherSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SwitcherSearchBox.Text?.ToLower() ?? "";
        if (string.IsNullOrEmpty(searchText))
        {
            SwitcherTabList.ItemsSource = _viewModel.Tabs;
        }
        else
        {
            var filtered = _viewModel.Tabs.Where(t =>
                t.Title.ToLower().Contains(searchText) ||
                t.WorkingDirectory.ToLower().Contains(searchText));
            SwitcherTabList.ItemsSource = filtered;
        }

        // Select first item if any
        if (SwitcherTabList.Items.Count > 0)
        {
            SwitcherTabList.SelectedIndex = 0;
        }
    }

    private void SwitcherSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (SwitcherTabList.SelectedIndex < SwitcherTabList.Items.Count - 1)
            {
                SwitcherTabList.SelectedIndex++;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (SwitcherTabList.SelectedIndex > 0)
            {
                SwitcherTabList.SelectedIndex--;
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
            TabSwitcherPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void SwitcherTabList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectSwitcherItem();
    }

    private void SelectSwitcherItem()
    {
        if (SwitcherTabList.SelectedItem is ITabViewModel tab)
        {
            _viewModel.SelectedTab = tab;
            TabSwitcherPopup.IsOpen = false;
        }
    }

    #endregion

    #region Tab Cycling

    private void CycleTab(bool forward)
    {
        if (_viewModel.Tabs.Count <= 1) return;

        var currentIndex = _viewModel.SelectedTab != null
            ? _viewModel.Tabs.IndexOf(_viewModel.SelectedTab)
            : 0;

        int newIndex;
        if (forward)
        {
            newIndex = (currentIndex + 1) % _viewModel.Tabs.Count;
        }
        else
        {
            newIndex = (currentIndex - 1 + _viewModel.Tabs.Count) % _viewModel.Tabs.Count;
        }

        _viewModel.SelectedTab = _viewModel.Tabs[newIndex];
    }

    #endregion
}
