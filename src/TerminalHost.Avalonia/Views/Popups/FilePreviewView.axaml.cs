using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class FilePreviewView : UserControl
{
    public FilePreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            if (control.DataContext is FilePreviewViewModel newViewModel)
            {
                // Unsubscribe from old view model if any
                if (sender is FilePreviewView view && view.DataContext != newViewModel)
                {
                    // Clean up old subscriptions
                }

                newViewModel.ScrollToLineRequested += OnScrollToLineRequested;
            }
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var viewModel = DataContext as FilePreviewViewModel;
        if (viewModel == null) return;

        if (e.Key == Key.Escape)
        {
            viewModel.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnScrollToLineRequested(object? sender, int lineNumber)
    {
        // In Avalonia with SelectableTextBlock, we can't directly scroll to a line number
        // as easily as with FlowDocument. This is a simplified implementation.
        // For a more accurate implementation, you might need to:
        // 1. Calculate line height
        // 2. Scroll to estimated position
        // 3. Or use a different control that supports line-based scrolling

        Dispatcher.UIThread.Post(() =>
        {
            // Find the ScrollViewer
            var scrollViewer = this.FindControl<ScrollViewer>("ContentScrollViewer");
            var textBlock = this.FindControl<SelectableTextBlock>("ContentTextBlock");

            if (scrollViewer != null && textBlock != null)
            {
                // Estimate scroll position based on line number
                // This is approximate - actual line height may vary
                var estimatedLineHeight = 16; // Approximate line height in pixels
                var targetOffset = (lineNumber - 1) * estimatedLineHeight;

                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetOffset);
            }
        }, DispatcherPriority.Background);
    }
}