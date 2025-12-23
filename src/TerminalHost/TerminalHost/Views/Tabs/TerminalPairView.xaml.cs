using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TerminalHost.Controls;
using TerminalHost.Domain;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Tabs;

public partial class TerminalPairView : UserControl
{
    private TerminalPairTabViewModel? _currentViewModel;
    private PanelWindow? _panelWindow;

    public TerminalPairView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Subscribe to PanelHost events
        if (RightPanelHost != null)
        {
            RightPanelHost.PanelCloseRequested += OnPanelCloseRequested;
            RightPanelHost.PanelUndockRequested += OnPanelUndockRequested;
            RightPanelHost.PanelDetachRequested += OnPanelDetachRequested;
        }

        // Subscribe to PanelPopup events
        if (PanelPopupHost != null)
        {
            PanelPopupHost.DockRequested += OnPopupDockRequested;
            PanelPopupHost.PopOutRequested += OnPopupPopOutRequested;
        }
    }

    private void OnPanelCloseRequested(object? sender, IPanelableViewModel panel)
    {
        if (_currentViewModel == null) return;

        // Hide the docked panel based on panel type
        HideDockPanel(panel);
    }

    private void OnPanelUndockRequested(object? sender, IPanelableViewModel panel)
    {
        if (_currentViewModel == null) return;

        // Hide the docked panel
        HideDockPanel(panel);

        // Show as floating popup
        ShowPanelPopup(panel);
    }

    private void OnPanelDetachRequested(object? sender, IPanelableViewModel panel)
    {
        if (_currentViewModel == null) return;

        // Hide the docked panel
        HideDockPanel(panel);

        // Show as detached window
        ShowPanelWindow(panel);
    }

    private void HideDockPanel(IPanelableViewModel panel)
    {
        if (_currentViewModel == null) return;

        // Remove panel from the docked collection
        _currentViewModel.RemovePanel(panel);

        // Only hide the panel area if no panels remain
        if (_currentViewModel.RightPanels.Count == 0)
        {
            _currentViewModel.IsExplorerVisible = false;
        }
    }

    private void ShowDockPanel(IPanelableViewModel panel)
    {
        if (_currentViewModel == null) return;

        switch (panel.PanelId)
        {
            case "fileExplorer":
            case "markdownPreview":
            case "gitChanges":
            case "scratchPad":
                // Show the right panel and make this panel active
                _currentViewModel.IsExplorerVisible = true;
                _currentViewModel.ActiveRightPanel = panel;
                break;
        }
    }

    private void ShowPanelPopup(IPanelableViewModel panel)
    {
        // If there's already a different popup showing, dock it back first
        if (PanelPopupHost.DataContext is IPanelableViewModel currentPopupPanel && currentPopupPanel != panel)
        {
            currentPopupPanel.IsOpen = false;
            currentPopupPanel.DisplayState = PanelDisplayState.Panel;
            ShowDockPanel(currentPopupPanel);
        }

        // Set up popup and open it
        panel.DisplayState = PanelDisplayState.Popup;
        panel.IsOpen = true;
        PanelPopupHost.DataContext = panel;
    }

    private void ShowPanelWindow(IPanelableViewModel panel)
    {
        // If there's a popup showing for a different panel, dock it back first
        if (PanelPopupHost.DataContext is IPanelableViewModel currentPopupPanel && currentPopupPanel != panel)
        {
            currentPopupPanel.IsOpen = false;
            currentPopupPanel.DisplayState = PanelDisplayState.Panel;
            ShowDockPanel(currentPopupPanel);
        }
        else if (PanelPopupHost.DataContext is IPanelableViewModel samePopupPanel)
        {
            // Same panel transitioning from popup to window - just close popup
            samePopupPanel.IsOpen = false;
        }
        PanelPopupHost.DataContext = null;

        // Close existing window if any (different panel)
        if (_panelWindow != null && _panelWindow.DataContext != panel)
        {
            _panelWindow.DockRequested -= OnPanelWindowDockRequested;
            _panelWindow.Close();
        }

        // Create new generic panel window
        panel.DisplayState = PanelDisplayState.Window;
        _panelWindow = new PanelWindow
        {
            DataContext = panel,
            Owner = Window.GetWindow(this),
            Width = panel.Width,
            Height = panel.Height
        };

        _panelWindow.DockRequested += OnPanelWindowDockRequested;
        _panelWindow.Closed += (s, e) =>
        {
            // When window is closed via X button (not dock), reset state
            // so the next toggle shows the docked panel
            if (_panelWindow?.DataContext is IPanelableViewModel closedPanel)
            {
                closedPanel.DisplayState = PanelDisplayState.Panel;
            }
            _panelWindow = null;
        };

        _panelWindow.Show();
    }

    private void OnPopupDockRequested(object? sender, IPanelableViewModel panel)
    {
        if (_currentViewModel == null) return;

        // Clear popup
        PanelPopupHost.DataContext = null;

        // Show the docked panel again
        panel.DisplayState = PanelDisplayState.Panel;
        ShowDockPanel(panel);
    }

    private void OnPopupPopOutRequested(object? sender, IPanelableViewModel panel)
    {
        if (_currentViewModel == null) return;

        // Clear popup
        PanelPopupHost.DataContext = null;

        // Show as window instead
        ShowPanelWindow(panel);
    }

    private void OnPanelWindowDockRequested(object? sender, IPanelableViewModel panel)
    {
        if (_currentViewModel == null) return;

        // Show the docked panel again
        panel.DisplayState = PanelDisplayState.Panel;
        ShowDockPanel(panel);

        _panelWindow = null;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from old view model
        if (_currentViewModel != null)
        {
            _currentViewModel.PanelStateChangeRequested -= OnPanelStateChangeRequested;
            _currentViewModel.ExplorerToggleRequested -= OnExplorerToggleRequested;
            _currentViewModel.MarkdownPreviewToggleRequested -= OnMarkdownPreviewToggleRequested;
        }

        // Subscribe to new view model
        if (e.NewValue is TerminalPairTabViewModel vm)
        {
            _currentViewModel = vm;
            vm.PanelStateChangeRequested += OnPanelStateChangeRequested;
            vm.ExplorerToggleRequested += OnExplorerToggleRequested;
            vm.MarkdownPreviewToggleRequested += OnMarkdownPreviewToggleRequested;
        }
        else
        {
            _currentViewModel = null;
        }
    }

    private void OnExplorerToggleRequested(object? sender, ExplorerToggleEventArgs e)
    {
        // Check if explorer is in popup state and actually open
        if (PanelPopupHost.DataContext is FileExplorerPanelViewModel popupPanel && popupPanel.IsOpen)
        {
            // Popup is open - close it and dock back
            popupPanel.IsOpen = false;
            popupPanel.DisplayState = PanelDisplayState.Panel;
            PanelPopupHost.DataContext = null;
            ShowDockPanel(popupPanel);
            e.Handled = true;
            return;
        }

        // Clean up stale popup reference
        if (PanelPopupHost.DataContext is FileExplorerPanelViewModel stalePopup && !stalePopup.IsOpen)
        {
            stalePopup.DisplayState = PanelDisplayState.Panel;
            PanelPopupHost.DataContext = null;
        }

        // Check if explorer is in window state
        if (_panelWindow != null && _panelWindow.DataContext is FileExplorerPanelViewModel)
        {
            // Focus the window
            _panelWindow.Activate();
            _panelWindow.Focus();
            e.Handled = true;
            return;
        }

        // Otherwise, let the default toggle behavior happen
    }

    private void OnMarkdownPreviewToggleRequested(object? sender, PanelToggleEventArgs e)
    {
        if (_currentViewModel == null || e.Panel == null) return;

        var panel = e.Panel;

        // Check if markdown preview is in popup state
        if (PanelPopupHost.DataContext is MarkdownPreviewViewModel popupPanel && popupPanel.IsOpen)
        {
            // Popup is open - close it and dock back
            popupPanel.IsOpen = false;
            popupPanel.DisplayState = PanelDisplayState.Panel;
            PanelPopupHost.DataContext = null;
            ShowDockPanel(popupPanel);
            e.Handled = true;
            return;
        }

        // Clean up stale popup reference
        if (PanelPopupHost.DataContext is MarkdownPreviewViewModel stalePopup && !stalePopup.IsOpen)
        {
            stalePopup.DisplayState = PanelDisplayState.Panel;
            PanelPopupHost.DataContext = null;
        }

        // Check if markdown preview is in window state
        if (_panelWindow != null && _panelWindow.DataContext is MarkdownPreviewViewModel)
        {
            // Focus the window
            _panelWindow.Activate();
            _panelWindow.Focus();
            e.Handled = true;
            return;
        }

        // Not in popup or window state - default behavior will toggle the docked panel
    }

    private void OnPanelStateChangeRequested(object? sender, PanelStateChangeRequestedEventArgs e)
    {
        if (_currentViewModel == null || sender is not IPanelableViewModel panel)
            return;

        switch (e.RequestedState)
        {
            case PanelDisplayState.Panel:
                // Dock back to panel - already docked, just update side if needed
                if (e.DockSide.HasValue)
                {
                    panel.PreferredSide = e.DockSide.Value;
                }
                break;

            case PanelDisplayState.Popup:
                // TODO: Transition to popup mode - for now just close the panel
                // This would require creating a popup window
                break;

            case PanelDisplayState.Window:
                // TODO: Transition to window mode - for now just close the panel
                // This would require creating a detached window
                break;
        }
    }

    private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Update the view model with the new split ratio (horizontal mode)
        if (DataContext is TerminalPairTabViewModel terminalTab)
        {
            // Find the MainTerminalsGrid to get actual column widths
            var mainGrid = FindName("MainTerminalsGrid") as Grid;
            if (mainGrid != null && mainGrid.ColumnDefinitions.Count >= 3)
            {
                var customWidth = mainGrid.ColumnDefinitions[0].ActualWidth;
                var shellWidth = mainGrid.ColumnDefinitions[2].ActualWidth;
                terminalTab.UpdateSplitRatioFromColumnWidths(customWidth, shellWidth);
            }
        }
    }

    private void VerticalSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Update the view model with the new split ratio (vertical mode)
        if (DataContext is TerminalPairTabViewModel terminalTab)
        {
            // Find the MainTerminalsGrid to get actual row heights
            var mainGrid = FindName("MainTerminalsGrid") as Grid;
            if (mainGrid != null && mainGrid.RowDefinitions.Count >= 3)
            {
                var customHeight = mainGrid.RowDefinitions[0].ActualHeight;
                var shellHeight = mainGrid.RowDefinitions[2].ActualHeight;
                var total = customHeight + shellHeight;
                if (total > 0)
                {
                    terminalTab.SplitRatio = customHeight / total;
                }
            }
        }
    }

    private void RunSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Update the view model with the new run split ratio
        if (DataContext is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            // Find the MainContentGrid to get actual column widths
            var mainContentGrid = FindName("MainContentGrid") as Grid;
            if (mainContentGrid != null && mainContentGrid.ColumnDefinitions.Count >= 3)
            {
                // Columns: main terminals (0), run (2)
                var mainWidth = mainContentGrid.ColumnDefinitions[0].ActualWidth;
                var runWidth = mainContentGrid.ColumnDefinitions[2].ActualWidth;
                var totalWidth = mainWidth + runWidth;

                if (totalWidth > 0)
                {
                    // Run ratio is portion of main content area
                    terminalTab.RunSplitRatio = runWidth / totalWidth;
                }
            }
        }
    }

    private void ExplorerSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Update the view model with the new explorer split ratio
        if (DataContext is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            // Find the parent Grid (outer grid with 3 columns)
            if (splitter.Parent is Grid grid && grid.ColumnDefinitions.Count >= 3)
            {
                // Columns: main content (0), explorer (2)
                var mainWidth = grid.ColumnDefinitions[0].ActualWidth;
                var explorerWidth = grid.ColumnDefinitions[2].ActualWidth;
                var totalWidth = mainWidth + explorerWidth;

                if (totalWidth > 0)
                {
                    // Explorer ratio is portion of total
                    terminalTab.ExplorerSplitRatio = explorerWidth / totalWidth;
                }
            }
        }
    }

    private void OpenDetectedRunUrl_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalPairTabViewModel terminalTab && !string.IsNullOrEmpty(terminalTab.DetectedRunUrl))
        {
             // Access MainViewModel via Window.DataContext
             if (Application.Current.MainWindow?.DataContext is MainViewModel mainViewModel)
             {
                 mainViewModel.RunUrlDetectionService.OpenInBrowser(terminalTab.DetectedRunUrl);
             }
        }
    }
}
