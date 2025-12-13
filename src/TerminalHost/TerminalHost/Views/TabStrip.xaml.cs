using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.ViewModels; // For ITabViewModel

namespace TerminalHost.Views;

public partial class TabStrip : UserControl
{
    // Drag-and-drop tab reordering
    private Point _dragStartPoint;
    private ITabViewModel? _draggedTab;

    public TabStrip()
    {
        InitializeComponent();
    }

    #region Tab Drag-Drop and Middle-Click

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Middle-click to close tab
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            if (sender is FrameworkElement element && element.DataContext is ITabViewModel tab)
            {
                // This command is on MainViewModel, so we need to access it from Window.DataContext
                if (Window.GetWindow(this)?.DataContext is MainViewModel mainViewModel)
                {
                    mainViewModel.CloseTabCommand.Execute(tab);
                    e.Handled = true;
                }
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

            // Access MainViewModel to move tabs in ObservableCollection
            if (Window.GetWindow(this)?.DataContext is MainViewModel mainViewModel)
            {
                var oldIndex = mainViewModel.Tabs.IndexOf(droppedTab);
                var newIndex = mainViewModel.Tabs.IndexOf(targetTab);

                if (oldIndex >= 0 && newIndex >= 0)
                {
                    mainViewModel.Tabs.Move(oldIndex, newIndex);
                }
            }
        }

        e.Handled = true;
    }

    #endregion

    #region Tab Overflow

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
            // This is a fallback, ideally a ScrollViewer is always found within a ListBox template
            var mainViewModel = Window.GetWindow(this)?.DataContext as MainViewModel;
            var hasOverflow = mainViewModel?.Tabs.Count > 5; // Arbitrary threshold for rough visibility
            ScrollLeftButton.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            ScrollRightButton.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var hasHorizontalOverflow = scrollViewer.ExtentWidth > scrollViewer.ViewportWidth;
        ScrollLeftButton.Visibility = hasHorizontalOverflow ? Visibility.Visible : Visibility.Collapsed;
        ScrollRightButton.Visibility = hasHorizontalOverflow ? Visibility.Visible : Visibility.Collapsed;
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

    #endregion
}
