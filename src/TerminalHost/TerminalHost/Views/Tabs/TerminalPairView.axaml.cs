using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Tabs;

public partial class TerminalPairView : UserControl
{
    public TerminalPairView()
    {
        InitializeComponent();
    }

    private void GridSplitter_DragCompleted(object? sender, VectorEventArgs e)
    {
        // Update the view model with the new split ratio (horizontal mode)
        if (DataContext is TerminalPairTabViewModel terminalTab)
        {
            // Find the MainTerminalsGrid to get actual column widths
            var mainGrid = this.FindControl<Grid>("MainTerminalsGrid");
            if (mainGrid != null && mainGrid.ColumnDefinitions.Count >= 3)
            {
                var customWidth = mainGrid.ColumnDefinitions[0].ActualWidth;
                var shellWidth = mainGrid.ColumnDefinitions[2].ActualWidth;
                terminalTab.UpdateSplitRatioFromColumnWidths(customWidth, shellWidth);
            }
        }
    }

    private void VerticalSplitter_DragCompleted(object? sender, VectorEventArgs e)
    {
        // Update the view model with the new split ratio (vertical mode)
        if (DataContext is TerminalPairTabViewModel terminalTab)
        {
            // Find the MainTerminalsGrid to get actual row heights
            var mainGrid = this.FindControl<Grid>("MainTerminalsGrid");
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

    private void RunSplitter_DragCompleted(object? sender, VectorEventArgs e)
    {
        // Update the view model with the new run split ratio
        if (DataContext is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            // Find the MainContentGrid to get actual column widths
            var mainContentGrid = this.FindControl<Grid>("MainContentGrid");
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

    private void ExplorerSplitter_DragCompleted(object? sender, VectorEventArgs e)
    {
        // Update the view model with the new explorer split ratio
        if (DataContext is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            // Find the parent Grid (outer grid with 3 columns)
            var outerGrid = splitter.Parent as Grid;
            if (outerGrid != null && outerGrid.ColumnDefinitions.Count >= 3)
            {
                // Columns: main content (0), explorer (2)
                var mainWidth = outerGrid.ColumnDefinitions[0].ActualWidth;
                var explorerWidth = outerGrid.ColumnDefinitions[2].ActualWidth;
                var totalWidth = mainWidth + explorerWidth;

                if (totalWidth > 0)
                {
                    // Explorer ratio is portion of total
                    terminalTab.ExplorerSplitRatio = explorerWidth / totalWidth;
                }
            }
        }
    }

    private void OpenDetectedRunUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalPairTabViewModel terminalTab && !string.IsNullOrEmpty(terminalTab.DetectedRunUrl))
        {
            // Access MainViewModel via Window.DataContext
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window?.DataContext is MainViewModel mainViewModel)
            {
                mainViewModel.RunUrlDetectionService.OpenInBrowser(terminalTab.DetectedRunUrl);
            }
        }
    }
}
