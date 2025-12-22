using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class TabStrip : UserControl
{
    // Drag-and-drop tab reordering
    private Point _dragStartPoint;
    private ITabViewModel? _draggedTab;

    public TabStrip()
    {
        InitializeComponent();
        Loaded += TabStrip_Loaded;
        DataContextChanged += TabStrip_DataContextChanged;
    }

    private void TabStrip_Loaded(object sender, RoutedEventArgs e)
    {
        // Update overflow buttons visibility on initial load
        UpdateOverflowButtonsVisibility();
    }

    private void TabStrip_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Subscribe to Tabs collection changes to update overflow buttons
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.Tabs.CollectionChanged -= Tabs_CollectionChanged;
        }
        if (e.NewValue is MainViewModel newVm)
        {
            newVm.Tabs.CollectionChanged += Tabs_CollectionChanged;
        }
    }

    private void Tabs_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Update overflow buttons when tabs are added/removed
        Dispatcher.BeginInvoke(new Action(() => UpdateOverflowButtonsVisibility()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    #region Tab Drag-Drop and Middle-Click

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Middle-click to close tab
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            if (sender is FrameworkElement element && element.DataContext is ITabViewModel tab)
            {
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

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
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

        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#0078D4"));
            border.BorderThickness = new Thickness(2, 2, 2, 0);
        }
    }

    private void Tab_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
        }
    }

    private void Tab_Drop(object sender, DragEventArgs e)
    {
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

        if (sender is FrameworkElement element && element.DataContext is ITabViewModel targetTab)
        {
            if (droppedTab == targetTab)
            {
                return;
            }

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
        TabList.ApplyTemplate();
        if (TabList.Template?.FindName("ScrollViewer", TabList) is ScrollViewer sv)
            return sv;

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
            var mainViewModel = Window.GetWindow(this)?.DataContext as MainViewModel;
            var hasOverflow = mainViewModel?.Tabs.Count > 5;
            var visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
            ScrollLeftButton.Visibility = visibility;
            ScrollRightButton.Visibility = visibility;
            TabDropdownButton.Visibility = visibility;
            return;
        }

        var hasHorizontalOverflow = scrollViewer.ExtentWidth > scrollViewer.ViewportWidth;
        var overflowVisibility = hasHorizontalOverflow ? Visibility.Visible : Visibility.Collapsed;
        ScrollLeftButton.Visibility = overflowVisibility;
        ScrollRightButton.Visibility = overflowVisibility;
        TabDropdownButton.Visibility = overflowVisibility;
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
