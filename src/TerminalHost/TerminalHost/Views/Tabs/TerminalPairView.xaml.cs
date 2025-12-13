using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Tabs;

public partial class TerminalPairView : UserControl
{
    public TerminalPairView()
    {
        InitializeComponent();
    }

    private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Update the view model with the new split ratio
        if (DataContext is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            // Find the parent Grid to get actual column widths
            if (splitter.Parent is Grid grid && grid.ColumnDefinitions.Count >= 3)
            {
                var customWidth = grid.ColumnDefinitions[0].ActualWidth;
                var shellWidth = grid.ColumnDefinitions[2].ActualWidth;
                terminalTab.UpdateSplitRatioFromColumnWidths(customWidth, shellWidth);
            }
        }
    }

    private void RunSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        // Update the view model with the new run split ratio
        if (DataContext is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            // Find the parent Grid to get actual column widths
            if (splitter.Parent is Grid grid && grid.ColumnDefinitions.Count >= 5)
            {
                // Main terminals are columns 0-2, run terminal is column 4
                var mainWidth = grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[2].ActualWidth;
                var runWidth = grid.ColumnDefinitions[4].ActualWidth;
                terminalTab.UpdateRunSplitRatioFromColumnWidths(mainWidth, runWidth);
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
