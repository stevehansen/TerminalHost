using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Tabs;

public partial class TerminalPairView : UserControl
{
    private TerminalPairTabViewModel? _currentViewModel;

    public TerminalPairView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_currentViewModel is not null && RightPanelHost is not null)
            _currentViewModel.AttachRightDock(RightPanelHost);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _currentViewModel = e.NewValue as TerminalPairTabViewModel;
        if (IsLoaded && _currentViewModel is not null && RightPanelHost is not null)
            _currentViewModel.AttachRightDock(RightPanelHost);
    }

    private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is TerminalPairTabViewModel terminalTab)
        {
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
        if (DataContext is TerminalPairTabViewModel terminalTab)
        {
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
        if (DataContext is TerminalPairTabViewModel terminalTab && sender is GridSplitter)
        {
            var mainContentGrid = FindName("MainContentGrid") as Grid;
            if (mainContentGrid != null && mainContentGrid.ColumnDefinitions.Count >= 3)
            {
                var mainWidth = mainContentGrid.ColumnDefinitions[0].ActualWidth;
                var runWidth = mainContentGrid.ColumnDefinitions[2].ActualWidth;
                var totalWidth = mainWidth + runWidth;

                if (totalWidth > 0)
                {
                    terminalTab.RunSplitRatio = runWidth / totalWidth;
                }
            }
        }
    }

    private void ExplorerSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is TerminalPairTabViewModel terminalTab && sender is GridSplitter splitter)
        {
            if (splitter.Parent is Grid grid && grid.ColumnDefinitions.Count >= 3)
            {
                var mainWidth = grid.ColumnDefinitions[0].ActualWidth;
                var explorerWidth = grid.ColumnDefinitions[2].ActualWidth;
                var totalWidth = mainWidth + explorerWidth;

                if (totalWidth > 0)
                {
                    terminalTab.ExplorerSplitRatio = explorerWidth / totalWidth;
                }
            }
        }
    }

    private void OpenDetectedRunUrl_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalPairTabViewModel terminalTab && !string.IsNullOrEmpty(terminalTab.DetectedRunUrl))
        {
            if (Application.Current.MainWindow?.DataContext is MainViewModel mainViewModel)
            {
                mainViewModel.RunUrlDetectionService.OpenInBrowser(terminalTab.DetectedRunUrl);
            }
        }
    }
}
