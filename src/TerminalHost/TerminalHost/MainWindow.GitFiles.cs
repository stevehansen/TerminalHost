using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace TerminalHost;

/// <summary>
/// Git files popup logic.
/// </summary>
public partial class MainWindow
{
    private readonly GitStatusService _gitStatusService = new();
    private List<Domain.GitFileStatus> _gitFiles = new();
    private string? _currentGitWorkingDirectory;
    private Domain.GitFileStatus? _selectedGitFile;
    private bool _isDraggingGitFiles;
    private Point _gitFilesDragStart;

    private async void ShowGitFiles()
    {
        // Get current working directory from selected terminal tab
        if (_viewModel.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            MessageBox.Show(
                "Please select a project tab first.",
                "Git Changes",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _currentGitWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        GitFilesTitle.Text = $"Git Changes - {terminalTab.Title}";
        GitFilesInfo.Text = _currentGitWorkingDirectory;

        // Load git files
        await RefreshGitFiles();

        // Center the popup on the window
        var windowPos = PointToScreen(new Point(0, 0));
        GitFilesPopup.HorizontalOffset = windowPos.X + (ActualWidth - 1100) / 2;
        GitFilesPopup.VerticalOffset = windowPos.Y + (ActualHeight - 700) / 2;

        GitFilesPopup.IsOpen = true;
    }

    private async Task RefreshGitFiles()
    {
        if (string.IsNullOrEmpty(_currentGitWorkingDirectory))
            return;

        _gitFiles = await _gitStatusService.GetModifiedFilesAsync(_currentGitWorkingDirectory);

        GitFilesList.ItemsSource = _gitFiles;
        GitFilesCount.Text = _gitFiles.Count == 1
            ? "1 file changed"
            : $"{_gitFiles.Count} files changed";

        GitFilesEmptyState.Visibility = _gitFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Clear selection
        _selectedGitFile = null;
        GitDiffTitle.Text = "Select a file to view diff";
        GitDiffContent.Document = new System.Windows.Documents.FlowDocument();
        UpdateGitFileButtons(false);

        // Auto-select first file if any
        if (_gitFiles.Count > 0)
        {
            GitFilesList.SelectedIndex = 0;
        }
    }

    private void UpdateGitFileButtons(bool hasSelection)
    {
        GitFilePreviewButton.IsEnabled = hasSelection && _selectedGitFile?.Status != Domain.GitFileStatusType.Deleted;
        GitFileEditButton.IsEnabled = hasSelection && _selectedGitFile?.Status != Domain.GitFileStatusType.Deleted;
        GitFileExplorerButton.IsEnabled = hasSelection;
    }

    private async void GitFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GitFilesList.SelectedItem is not Domain.GitFileStatus file)
        {
            _selectedGitFile = null;
            UpdateGitFileButtons(false);
            return;
        }

        _selectedGitFile = file;
        UpdateGitFileButtons(true);

        GitDiffTitle.Text = $"Diff: {file.FilePath}";

        // Load and display diff
        if (string.IsNullOrEmpty(_currentGitWorkingDirectory))
            return;

        var diff = await _gitStatusService.GetFileDiffAsync(_currentGitWorkingDirectory, file.FilePath, file.IsStaged);

        if (!string.IsNullOrEmpty(diff))
        {
            // Use diff highlighter to format
            var highlighter = new Services.SyntaxHighlighting.DiffHighlighter();
            var document = highlighter.CreateHighlightedDocument(diff, null);
            GitDiffContent.Document = document;
        }
        else
        {
            GitDiffContent.Document = CreateInfoDocument("No changes to display");
        }
    }

    private static System.Windows.Documents.FlowDocument CreateInfoDocument(string message)
    {
        var document = new System.Windows.Documents.FlowDocument
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80)),
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code NF, Consolas, Courier New"),
            FontSize = 13,
            PagePadding = new Thickness(16),
            PageWidth = 10000
        };

        var paragraph = new System.Windows.Documents.Paragraph();
        paragraph.Inlines.Add(new System.Windows.Documents.Run(message));
        document.Blocks.Add(paragraph);

        return document;
    }

    private void GitFilePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGitFile == null || string.IsNullOrEmpty(_currentGitWorkingDirectory))
            return;

        var fullPath = System.IO.Path.Combine(_currentGitWorkingDirectory, _selectedGitFile.FilePath);
        if (System.IO.File.Exists(fullPath))
        {
            ShowFilePreview(fullPath);
        }
    }

    private void GitFileEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGitFile == null || string.IsNullOrEmpty(_currentGitWorkingDirectory))
            return;

        var fullPath = System.IO.Path.Combine(_currentGitWorkingDirectory, _selectedGitFile.FilePath);
        if (System.IO.File.Exists(fullPath))
        {
            ShowFileEdit(fullPath);
        }
    }

    private void GitFileExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGitFile == null || string.IsNullOrEmpty(_currentGitWorkingDirectory))
            return;

        var fullPath = System.IO.Path.Combine(_currentGitWorkingDirectory, _selectedGitFile.FilePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);

        if (System.IO.Directory.Exists(directory))
        {
            // Open explorer and select the file if it exists
            if (System.IO.File.Exists(fullPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", directory);
            }
        }
    }

    private async void GitFilesRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshGitFiles();
    }

    private void GitFilesClose_Click(object sender, RoutedEventArgs e)
    {
        GitFilesPopup.IsOpen = false;
    }

    private void GitFilesHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingGitFiles = true;
        _gitFilesDragStart = PointToScreen(e.GetPosition(this));
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void GitFilesHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingGitFiles) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var diff = currentPos - _gitFilesDragStart;

        GitFilesPopup.HorizontalOffset += diff.X;
        GitFilesPopup.VerticalOffset += diff.Y;

        _gitFilesDragStart = currentPos;
        e.Handled = true;
    }

    private void GitFilesHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingGitFiles)
        {
            _isDraggingGitFiles = false;
            Mouse.Capture(null);
            e.Handled = true;
        }
    }

    private void GitFilesResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var newWidth = GitFilesBorder.Width + e.HorizontalChange;
        var newHeight = GitFilesBorder.Height + e.VerticalChange;

        if (newWidth >= GitFilesBorder.MinWidth)
        {
            GitFilesBorder.Width = newWidth;
        }
        if (newHeight >= GitFilesBorder.MinHeight)
        {
            GitFilesBorder.Height = newHeight;
        }
    }
}
