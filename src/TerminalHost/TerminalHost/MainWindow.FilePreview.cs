using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace TerminalHost;

/// <summary>
/// File preview popup logic.
/// </summary>
public partial class MainWindow
{
    private readonly FilePreviewService _filePreviewService = new();
    private string? _currentPreviewFilePath;
    private bool _isDraggingPreview;
    private Point _previewDragStart;

    private void OnFilePreviewRequested(object? sender, FilePreviewRequestedEventArgs e)
    {
        ShowFilePreview(e.FilePath, e.Line);
    }

    private void OpenFilePreviewDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select File to Preview",
            Filter = "All Files (*.*)|*.*|Code Files (*.cs;*.js;*.ts;*.py;*.json;*.xml)|*.cs;*.js;*.ts;*.py;*.json;*.xml|Text Files (*.txt;*.md;*.log)|*.txt;*.md;*.log",
            FilterIndex = 1
        };

        // Set initial directory to current tab's working directory
        if (_viewModel.SelectedTab is TerminalPairTabViewModel terminalTab)
        {
            dialog.InitialDirectory = terminalTab.Pair.WorkingDirectory;
        }

        if (dialog.ShowDialog() == true)
        {
            ShowFilePreview(dialog.FileName);
        }
    }

    public void ShowFilePreview(string filePath, int? highlightLine = null)
    {
        System.Console.WriteLine($"[FilePreview] ShowFilePreview called for: {filePath}");
        var result = _filePreviewService.LoadFilePreview(filePath, highlightLine);
        if (result == null)
        {
            System.Console.WriteLine("[FilePreview] LoadFilePreview returned null");
            return;
        }
        System.Console.WriteLine($"[FilePreview] Result: IsSuccess={result.IsSuccess}, Error={result.Error}");

        _currentPreviewFilePath = result.FilePath;

        if (result.IsSuccess)
        {
            FilePreviewTitle.Text = result.FileName;
            FilePreviewContent.Document = result.Document!;
            FilePreviewInfo.Text = $"{result.LineCount:N0} lines • {FormatFileSize(result.FileSize)}";

            if (highlightLine.HasValue && result.Document != null)
            {
                ScrollToLine(highlightLine.Value);
            }
        }
        else
        {
            FilePreviewTitle.Text = result.FileName;
            FilePreviewContent.Document = CreateErrorDocument(result.Error!);
            FilePreviewInfo.Text = $"Error • {FormatFileSize(result.FileSize)}";
        }

        // Center the popup on the window
        var windowPos = PointToScreen(new Point(0, 0));
        FilePreviewPopup.HorizontalOffset = windowPos.X + (ActualWidth - 900) / 2;
        FilePreviewPopup.VerticalOffset = windowPos.Y + (ActualHeight - 600) / 2;

        FilePreviewPopup.IsOpen = true;
    }

    private void FilePreviewHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingPreview = true;
        _previewDragStart = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void FilePreviewHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingPreview) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _previewDragStart;

        FilePreviewPopup.HorizontalOffset += diff.X;
        FilePreviewPopup.VerticalOffset += diff.Y;

        _previewDragStart = currentPos;
        e.Handled = true;
    }

    private void FilePreviewHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingPreview)
        {
            _isDraggingPreview = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void FilePreviewResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var newWidth = FilePreviewBorder.Width + e.HorizontalChange;
        var newHeight = FilePreviewBorder.Height + e.VerticalChange;

        // Respect min constraints only - no max limit
        if (newWidth >= FilePreviewBorder.MinWidth)
        {
            FilePreviewBorder.Width = newWidth;
        }
        if (newHeight >= FilePreviewBorder.MinHeight)
        {
            FilePreviewBorder.Height = newHeight;
        }
    }

    private void ScrollToLine(int lineNumber)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var scrollViewer = FilePreviewContent.Parent as System.Windows.Controls.ScrollViewer;
            if (scrollViewer != null && lineNumber > 20)
            {
                var approximateOffset = (lineNumber - 10) * 18;
                scrollViewer.ScrollToVerticalOffset(approximateOffset);
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static System.Windows.Documents.FlowDocument CreateErrorDocument(string error)
    {
        var document = new System.Windows.Documents.FlowDocument
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code NF, Consolas, Courier New"),
            FontSize = 13,
            PagePadding = new Thickness(16),
            PageWidth = 10000
        };

        var paragraph = new System.Windows.Documents.Paragraph();
        paragraph.Inlines.Add(new System.Windows.Documents.Run(error)
        {
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF1, 0x48, 0x48))
        });
        document.Blocks.Add(paragraph);

        return document;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }

    private void FilePreviewClose_Click(object sender, RoutedEventArgs e)
    {
        FilePreviewPopup.IsOpen = false;
    }

    private void FilePreviewOpenInEditor_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentPreviewFilePath) && System.IO.File.Exists(_currentPreviewFilePath))
        {
            FilePreviewPopup.IsOpen = false;
            ShowFileEdit(_currentPreviewFilePath);
        }
    }
}
