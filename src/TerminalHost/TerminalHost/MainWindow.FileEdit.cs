using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost;

/// <summary>
/// File edit popup logic.
/// </summary>
public partial class MainWindow
{
    private readonly FileEditService _fileEditService = new();
    private string? _currentEditFilePath;
    private System.Text.Encoding? _currentEditEncoding;
    private string? _originalContent;
    private bool _isFileModified;
    private bool _isDraggingEdit;
    private Point _editDragStart;

    private void OpenFileEditDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select File to Edit",
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
            ShowFileEdit(dialog.FileName);
        }
    }

    public void ShowFileEdit(string filePath, int? goToLine = null)
    {
        Console.WriteLine($"[FileEdit] ShowFileEdit called for: {filePath}");
        var result = _fileEditService.LoadFile(filePath);

        if (!result.IsSuccess)
        {
            MessageBox.Show(
                result.Error ?? "Unknown error loading file",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _currentEditFilePath = result.FilePath;
        _currentEditEncoding = result.Encoding;
        _originalContent = result.Content;
        _isFileModified = false;

        FileEditTitle.Text = result.FileName;
        FileEditModifiedIndicator.Visibility = Visibility.Collapsed;
        FileEditTextBox.Text = result.Content;
        FileEditInfo.Text = $"{result.LineCount:N0} lines • {FormatFileSize(result.FileSize)}";

        if (result.IsReadOnly)
        {
            FileEditInfo.Text += " • Read-only";
            FileEditSaveButton.IsEnabled = false;
        }
        else
        {
            FileEditSaveButton.IsEnabled = true;
        }

        UpdateLineNumbers();
        UpdateCursorInfo();

        // Center the popup on the window
        var windowPos = PointToScreen(new Point(0, 0));
        FileEditPopup.HorizontalOffset = windowPos.X + (ActualWidth - 1000) / 2;
        FileEditPopup.VerticalOffset = windowPos.Y + (ActualHeight - 700) / 2;

        FileEditPopup.IsOpen = true;
        FileEditTextBox.Focus();

        // Go to specific line if requested
        if (goToLine.HasValue)
        {
            GoToLine(goToLine.Value);
        }
    }

    private void GoToLine(int lineNumber)
    {
        var text = FileEditTextBox.Text;
        var lines = text.Split('\n');
        var targetLine = Math.Max(0, Math.Min(lineNumber - 1, lines.Length - 1));

        int charIndex = 0;
        for (int i = 0; i < targetLine; i++)
        {
            charIndex += lines[i].Length + 1; // +1 for newline
        }

        FileEditTextBox.CaretIndex = charIndex;
        FileEditTextBox.ScrollToLine(targetLine);
        FileEditTextBox.Focus();
    }

    private void UpdateLineNumbers()
    {
        var lineCount = FileEditTextBox.Text.Split('\n').Length;
        var lineNumbers = new System.Text.StringBuilder();
        for (int i = 1; i <= lineCount; i++)
        {
            lineNumbers.AppendLine(i.ToString());
        }
        FileEditLineNumbers.Text = lineNumbers.ToString().TrimEnd();
    }

    private void UpdateCursorInfo()
    {
        var text = FileEditTextBox.Text;
        var caretIndex = FileEditTextBox.CaretIndex;

        // Calculate line and column
        var textUpToCaret = text.Substring(0, Math.Min(caretIndex, text.Length));
        var line = textUpToCaret.Count(c => c == '\n') + 1;
        var lastNewline = textUpToCaret.LastIndexOf('\n');
        var column = lastNewline < 0 ? caretIndex + 1 : caretIndex - lastNewline;

        FileEditCursorInfo.Text = $"Ln {line}, Col {column}";
    }

    private void FileEditTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateLineNumbers();

        // Check if content has changed
        _isFileModified = FileEditTextBox.Text != _originalContent;
        FileEditModifiedIndicator.Visibility = _isFileModified ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FileEditTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Sync line number scroll with text editor scroll
        LineNumberScroller.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void FileEditTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+S to save
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveCurrentFile();
            e.Handled = true;
        }
        // Ctrl+G to go to line
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowGoToLineDialog();
            e.Handled = true;
        }
        // Update cursor info on navigation keys
        else if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right ||
                 e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown)
        {
            Dispatcher.BeginInvoke(new Action(UpdateCursorInfo), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void ShowGoToLineDialog()
    {
        var lineCount = FileEditTextBox.Text.Split('\n').Length;
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            $"Enter line number (1-{lineCount}):",
            "Go to Line",
            "1");

        if (!string.IsNullOrEmpty(input) && int.TryParse(input, out var lineNumber))
        {
            GoToLine(lineNumber);
        }
    }

    private void SaveCurrentFile()
    {
        if (string.IsNullOrEmpty(_currentEditFilePath))
            return;

        var result = _fileEditService.SaveFile(_currentEditFilePath, FileEditTextBox.Text, _currentEditEncoding);

        if (result.Success)
        {
            _originalContent = FileEditTextBox.Text;
            _isFileModified = false;
            FileEditModifiedIndicator.Visibility = Visibility.Collapsed;

            // Update file info
            var fileInfo = new System.IO.FileInfo(_currentEditFilePath);
            var lineCount = FileEditTextBox.Text.Split('\n').Length;
            FileEditInfo.Text = $"{lineCount:N0} lines • {FormatFileSize(fileInfo.Length)} • Saved";
        }
        else
        {
            MessageBox.Show(
                result.Error ?? "Unknown error saving file",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void FileEditSave_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentFile();
    }

    private void FileEditReload_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentEditFilePath))
            return;

        if (_isFileModified)
        {
            var result = MessageBox.Show(
                "You have unsaved changes. Reload and lose changes?",
                "Confirm Reload",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        var editResult = _fileEditService.ReloadFile(_currentEditFilePath);
        if (editResult.IsSuccess)
        {
            _originalContent = editResult.Content;
            FileEditTextBox.Text = editResult.Content;
            _isFileModified = false;
            FileEditModifiedIndicator.Visibility = Visibility.Collapsed;
            FileEditInfo.Text = $"{editResult.LineCount:N0} lines • {FormatFileSize(editResult.FileSize)} • Reloaded";
        }
        else
        {
            MessageBox.Show(
                editResult.Error ?? "Unknown error reloading file",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void FileEditClose_Click(object sender, RoutedEventArgs e)
    {
        CloseFileEdit();
    }

    private void CloseFileEdit()
    {
        if (_isFileModified)
        {
            var result = MessageBox.Show(
                "You have unsaved changes. Close without saving?",
                "Unsaved Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        FileEditPopup.IsOpen = false;
        _currentEditFilePath = null;
        _currentEditEncoding = null;
        _originalContent = null;
        _isFileModified = false;
    }

    private void FileEditHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingEdit = true;
        _editDragStart = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void FileEditHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingEdit) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _editDragStart;

        FileEditPopup.HorizontalOffset += diff.X;
        FileEditPopup.VerticalOffset += diff.Y;

        _editDragStart = currentPos;
        e.Handled = true;
    }

    private void FileEditHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingEdit)
        {
            _isDraggingEdit = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void FileEditResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var newWidth = FileEditBorder.Width + e.HorizontalChange;
        var newHeight = FileEditBorder.Height + e.VerticalChange;

        if (newWidth >= FileEditBorder.MinWidth)
        {
            FileEditBorder.Width = newWidth;
        }
        if (newHeight >= FileEditBorder.MinHeight)
        {
            FileEditBorder.Height = newHeight;
        }
    }
}
