using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Popups;

public partial class FilePreviewView : UserControl
{
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
            var filePreviewContent = FindVisualChild<FlowDocumentScrollViewer>(this);
            if (filePreviewContent == null || filePreviewContent.Document == null) return;

            int currentLine = 0;
            foreach (Block block in filePreviewContent.Document.Blocks)
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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }
}
