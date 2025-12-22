using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class TabStrip : UserControl
{
    // Drag-and-drop tab reordering
    private Point _dragStartPoint;
    private ITabViewModel? _draggedTab;
    private bool _isDragging;

    public TabStrip()
    {
        InitializeComponent();
        Loaded += TabStrip_Loaded;
        DataContextChanged += TabStrip_DataContextChanged;

        // Add drag-drop event handlers using AddHandler since they're in a DataTemplate
        AddHandler(DragDrop.DragOverEvent, Tab_DragOver);
        AddHandler(DragDrop.DragLeaveEvent, Tab_DragLeave);
        AddHandler(DragDrop.DropEvent, Tab_Drop);
    }

    private void TabStrip_Loaded(object? sender, RoutedEventArgs e)
    {
        // Update overflow buttons visibility on initial load
        UpdateOverflowButtonsVisibility();
    }

    private void TabStrip_DataContextChanged(object? sender, EventArgs e)
    {
        // Subscribe to Tabs collection changes to update overflow buttons
        if (DataContext is MainViewModel newVm)
        {
            newVm.Tabs.CollectionChanged += Tabs_CollectionChanged;
        }
    }

    private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Update overflow buttons when tabs are added/removed
        Dispatcher.UIThread.Post(() => UpdateOverflowButtonsVisibility(), DispatcherPriority.Loaded);
    }

    #region Tab Drag-Drop and Middle-Click

    private void Tab_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;

        // Middle-click to close tab
        if (properties.IsMiddleButtonPressed)
        {
            if (sender is Control element && element.DataContext is ITabViewModel tab)
            {
                if (TopLevel.GetTopLevel(this)?.DataContext is MainViewModel mainViewModel)
                {
                    mainViewModel.CloseTabCommand.Execute(tab);
                    e.Handled = true;
                }
            }
            return;
        }

        // Left-click - prepare for drag
        if (properties.IsLeftButtonPressed)
        {
            if (sender is Control element && element.DataContext is ITabViewModel tab)
            {
                _dragStartPoint = e.GetPosition(null);
                _draggedTab = tab;
                _isDragging = false;
            }
        }
    }

    private async void Tab_PointerMoved(object? sender, PointerEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;

        if (!properties.IsLeftButtonPressed || _draggedTab == null || _isDragging)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        var diff = _dragStartPoint - currentPosition;

        // Check if drag threshold is exceeded (using a reasonable default)
        if (Math.Abs(diff.X) > 5 || Math.Abs(diff.Y) > 5)
        {
            _isDragging = true;
            var dragData = new DataObject();
            dragData.Set("TabViewModel", _draggedTab);

            if (sender is Control control)
            {
                await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
            }

            _draggedTab = null;
            _isDragging = false;
        }
    }

    private void Tab_DragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("TabViewModel"))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;

        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.Parse("#0078D4"));
            border.BorderThickness = new Thickness(2, 2, 2, 0);
        }
    }

    private void Tab_DragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
        }
    }

    private void Tab_Drop(object? sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
        }

        if (!e.Data.Contains("TabViewModel"))
        {
            return;
        }

        var droppedTab = e.Data.Get("TabViewModel") as ITabViewModel;
        if (droppedTab == null)
        {
            return;
        }

        if (sender is Control element && element.DataContext is ITabViewModel targetTab)
        {
            if (droppedTab == targetTab)
            {
                return;
            }

            if (TopLevel.GetTopLevel(this)?.DataContext is MainViewModel mainViewModel)
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

        // Search visual tree directly for ScrollViewer

        // Search visual tree
        foreach (var child in TabList.GetVisualDescendants())
        {
            if (child is ScrollViewer scrollViewer)
                return scrollViewer;
        }

        return null;
    }

    private void TabList_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateOverflowButtonsVisibility();
    }

    private void UpdateOverflowButtonsVisibility()
    {
        var scrollViewer = GetTabListScrollViewer();
        if (scrollViewer == null)
        {
            var mainViewModel = TopLevel.GetTopLevel(this)?.DataContext as MainViewModel;
            var hasOverflow = mainViewModel?.Tabs.Count > 5;
            var visibility = hasOverflow;
            ScrollLeftButton.IsVisible = visibility;
            ScrollRightButton.IsVisible = visibility;
            TabDropdownButton.IsVisible = visibility;
            return;
        }

        var hasHorizontalOverflow = scrollViewer.Extent.Width > scrollViewer.Viewport.Width;
        ScrollLeftButton.IsVisible = hasHorizontalOverflow;
        ScrollRightButton.IsVisible = hasHorizontalOverflow;
        TabDropdownButton.IsVisible = hasHorizontalOverflow;
    }

    private void ScrollLeft_Click(object? sender, RoutedEventArgs e)
    {
        var scrollViewer = GetTabListScrollViewer();
        if (scrollViewer != null)
        {
            scrollViewer.Offset = scrollViewer.Offset.WithX(scrollViewer.Offset.X - 150);
        }
    }

    private void ScrollRight_Click(object? sender, RoutedEventArgs e)
    {
        var scrollViewer = GetTabListScrollViewer();
        if (scrollViewer != null)
        {
            scrollViewer.Offset = scrollViewer.Offset.WithX(scrollViewer.Offset.X + 150);
        }
    }

    #endregion
}
