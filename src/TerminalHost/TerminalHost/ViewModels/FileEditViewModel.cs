using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TerminalHost.Domain;
using TerminalHost.Services;
using TerminalHost.ViewModels;

namespace TerminalHost.ViewModels;

public partial class FileEditViewModel : ObservableObject
{
    private readonly IFileEditService _fileEditService;
    private readonly IFileSystem _fileSystem;
    private string? _currentEditFilePath;
    private System.Text.Encoding? _currentEditEncoding;
    private string? _originalContent;

    [ObservableProperty]
    private string _title = "File Edit";

    [ObservableProperty]
    private string _content = "";

    partial void OnContentChanged(string value)
    {
        OnTextChanged();
    }

    [ObservableProperty]
    private string _lineNumbers = "";

    [ObservableProperty]
    private string _info = "";

    [ObservableProperty]
    private string _cursorInfo = "";

    [ObservableProperty]
    private bool _isFileModified;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private bool _canSave;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private double _width = 1000;

    [ObservableProperty]
    private double _height = 700;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    // We'll need a way to interact with the TextBox for caret position/scrolling.
    // For MVVM purity, this might require an attached behavior or event passing.
    // For now, we'll expose events that the View can subscribe to.
    public event EventHandler<int>? ScrollToLineRequested;
    public event EventHandler<int>? SetCaretIndexRequested;

    public FileEditViewModel(IFileEditService fileEditService, IFileSystem fileSystem)
    {
        _fileEditService = fileEditService;
        _fileSystem = fileSystem;
    }

    public void Open(string filePath, int? goToLine = null)
    {
        var result = _fileEditService.LoadFile(filePath);

        if (!result.IsSuccess)
        {
            DialogService.ShowError(result.Error ?? "Unknown error loading file");
            return;
        }

        _currentEditFilePath = result.FilePath;
        _currentEditEncoding = result.Encoding;
        _originalContent = result.Content;
        Content = result.Content ?? "";
        IsFileModified = false;

        Title = result.FileName;
        
        IsReadOnly = result.IsReadOnly;
        CanSave = !result.IsReadOnly;

        UpdateInfo(result.LineCount, result.FileSize, result.IsReadOnly);
        UpdateLineNumbers();
        
        IsOpen = true;

        if (goToLine.HasValue)
        {
            GoToLine(goToLine.Value);
        }
    }

    [RelayCommand]
    private void OpenDialog(string initialDirectory)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select File to Edit",
            Filter = "All Files (*.*)|*.*|Code Files (*.cs;*.js;*.ts;*.py;*.json;*.xml)|*.cs;*.js;*.ts;*.py;*.json;*.xml|Text Files (*.txt;*.md;*.log)|*.txt;*.md;*.log",
            FilterIndex = 1,
            InitialDirectory = initialDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            Open(dialog.FileName);
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrEmpty(_currentEditFilePath)) return;

        var result = _fileEditService.SaveFile(_currentEditFilePath, Content, _currentEditEncoding);

        if (result.Success)
        {
            _originalContent = Content;
            IsFileModified = false;

            // Update file info
            var fileSize = _fileSystem.GetFileSize(_currentEditFilePath);
            var lineCount = Content.Split('\n').Length;
            Info = $"{lineCount:N0} lines • {FormatFileSize(fileSize)} • Saved";
        }
        else
        {
            DialogService.ShowError(result.Error ?? "Unknown error saving file");
        }
    }

    [RelayCommand]
    private void Reload()
    {
        if (string.IsNullOrEmpty(_currentEditFilePath)) return;

        if (IsFileModified)
        {
            if (!DialogService.ShowConfirmation(
                "You have unsaved changes. Reload and lose changes?",
                "Confirm Reload"))
                return;
        }

        var editResult = _fileEditService.ReloadFile(_currentEditFilePath);
        if (editResult.IsSuccess)
        {
            _originalContent = editResult.Content;
            Content = editResult.Content ?? "";
            IsFileModified = false;
            
            UpdateInfo(editResult.LineCount, editResult.FileSize, IsReadOnly, "Reloaded");
            UpdateLineNumbers();
        }
        else
        {
            DialogService.ShowError(editResult.Error ?? "Unknown error reloading file");
        }
    }

    [RelayCommand]
    public void Close()
    {
        if (IsFileModified)
        {
            if (!DialogService.ShowConfirmation(
                "You have unsaved changes. Close without saving?",
                "Unsaved Changes"))
                return;
        }

        IsOpen = false;
        _currentEditFilePath = null;
        _currentEditEncoding = null;
        _originalContent = null;
        IsFileModified = false;
        Content = "";
    }

    [RelayCommand]
    public void ShowGoToLineDialog()
    {
        var lineCount = Content.Split('\n').Length;
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            $"Enter line number (1-{{lineCount}}):",
            "Go to Line",
            "1");

        if (!string.IsNullOrEmpty(input) && int.TryParse(input, out var lineNumber))
        {
            GoToLine(lineNumber);
        }
    }

    private void GoToLine(int lineNumber)
    {
        var lines = Content.Split('\n');
        var targetLine = Math.Max(0, Math.Min(lineNumber - 1, lines.Length - 1));

        int charIndex = 0;
        for (int i = 0; i < targetLine; i++)
        {
            charIndex += lines[i].Length + 1; // +1 for newline
        }

        SetCaretIndexRequested?.Invoke(this, charIndex);
        ScrollToLineRequested?.Invoke(this, targetLine);
    }

    [RelayCommand]
    private void OnTextChanged()
    {
        IsFileModified = Content != _originalContent;
        UpdateLineNumbers();
    }

    private void UpdateLineNumbers()
    {
        var lineCount = Content.Split('\n').Length;
        var sb = new StringBuilder();
        for (int i = 1; i <= lineCount; i++)
        {
            sb.AppendLine(i.ToString());
        }
        LineNumbers = sb.ToString().TrimEnd();
    }

    public void UpdateCursorInfo(int caretIndex)
    {
        var text = Content;
        // Calculate line and column
        var textUpToCaret = text.Substring(0, Math.Min(caretIndex, text.Length));
        var line = textUpToCaret.Count(c => c == '\n') + 1;
        var lastNewline = textUpToCaret.LastIndexOf('\n');
        var column = lastNewline < 0 ? caretIndex + 1 : caretIndex - lastNewline;

        CursorInfo = $"Ln {line}, Col {column}";
    }

    private void UpdateInfo(int lineCount, long fileSize, bool isReadOnly, string? status = null)
    {
        var info = $"{lineCount:N0} lines • {FormatFileSize(fileSize)}";
        if (isReadOnly) info += " • Read-only";
        if (status != null) info += " • " + status;
        Info = info;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}
