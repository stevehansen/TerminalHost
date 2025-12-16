using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class FilePreviewView : UserControl
{
    private bool _isDragging;
    private Point _dragStartPoint;

    public FilePreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is FilePreviewViewModel oldViewModel)
        {
            oldViewModel.ScrollToLineRequested -= OnScrollToLineRequested;
        }

        if (e.NewValue is FilePreviewViewModel newViewModel)
        {
            newViewModel.ScrollToLineRequested += OnScrollToLineRequested;
        }
    }

    private void OnScrollToLineRequested(object? sender, int lineNumber)
    {
        // FlowDocumentScrollViewer doesn't have ScrollToLine directly.
        // We'll need to find the paragraph for the line number and scroll to it.
        // Assuming each line is a paragraph in the FlowDocument (due to previous SyntaxHighlighterBase changes).

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (FilePreviewContent.Document == null) return;

            int currentLine = 0;
            foreach (Block block in FilePreviewContent.Document.Blocks)
            {
                if (block is Table table)
                {
                    foreach (TableRowGroup rowGroup in table.RowGroups)
                    {
                        foreach (TableRow row in rowGroup.Rows)
                        {
                            currentLine++;
                            if (currentLine == lineNumber)
                            {
                                // Assuming the second cell of the row contains the content paragraph
                                if (row.Cells.Count > 1 && row.Cells[1].Blocks.FirstBlock is Paragraph targetParagraph)
                                {
                                    targetParagraph.BringIntoView();
                                    return;
                                }
                            }
                        }
                    }
                }
                // Fallback for simple documents without tables (e.g. error messages)
                else if (block is Paragraph paragraph)
                {
                    currentLine++;
                    if (currentLine == lineNumber)
                    {
                        paragraph.BringIntoView();
                        return;
                    }
                }
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void FilePreviewHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var viewModel = DataContext as FilePreviewViewModel;
        if (viewModel == null) return;

        _isDragging = true;
        _dragStartPoint = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void FilePreviewHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _dragStartPoint;

        var viewModel = DataContext as FilePreviewViewModel;
        if (viewModel != null)
        {
            viewModel.HorizontalOffset += diff.X;
            viewModel.VerticalOffset += diff.Y;
        }

        _dragStartPoint = currentPos;
        e.Handled = true;
    }

    private void FilePreviewHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void FilePreviewResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var viewModel = DataContext as FilePreviewViewModel;
        if (viewModel == null) return;

        var newWidth = viewModel.Width + e.HorizontalChange;
        var newHeight = viewModel.Height + e.VerticalChange;

        if (newWidth >= 500) viewModel.Width = newWidth;
        if (newHeight >= 400) viewModel.Height = newHeight;
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is FilePreviewViewModel viewModel)
            {
                if (viewModel.CloseCommand.CanExecute(null))
                {
                    viewModel.CloseCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
